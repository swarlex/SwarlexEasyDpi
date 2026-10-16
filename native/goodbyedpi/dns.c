/*
 * SwarlexEasyDpi: sifreli DNS (DNS-over-HTTPS), sistem DNS ayarina dokunmadan.
 *
 * Disari giden duz DNS sorgulari (UDP 53) WinDivert ile yakalanir, sorgu oldugu gibi
 * https://1.1.1.1/dns-query'ye (yedek: 1.0.0.1, 8.8.8.8) HTTPS ile yollanir ve gelen cevap,
 * sanki DNS sunucusundan gelmis gibi geri enjekte edilir. Boylece ISS'nin zehirli DNS
 * cevaplari kullanilmaz ve sorgular sifreli gider. DoH sunucusuna IP ile baglanilir: kendi
 * adini cozmek icin DNS gerekmez (dongu olmaz).
 *
 * Saglamlik: DoH olmazsa sorgu oldugu gibi birakilir (sistem DNS'i cevaplar). Ust uste
 * basarisizlikta DoH 30 sn atlanir (sorgular beklemeden gecer). Ayni anda en fazla 64 is;
 * fazlasi beklemeden birakilir. Alma hatasi yakalamayi bitirmez (yalniz kapatma bitirir).
 * VPN/CGNAT (100.64.0.0/10) ve baglanti-yerel DNS sunuculari yakalanmaz.
 *
 * Fikir: umutxyp/Shroudly (MIT) - "isimleri sifreli DNS ile coz". Bu dosya ozgun koddur.
 *
 *   dns_start()     -> 0 ya da Windows hata kodu
 *   dns_stop()      -> beklemeden durdurur; tutamac son is bitince kapanir
 *   dns_selftest()  -> discord.com icin DoH cevabindaki kayit sayisi ya da -1
 *
 * Yalniz standart sorgular (opcode 0) DoH'a gider. DoH "ad yok / reddedildi / sunucu hatasi"
 * derse sorgu sistem DNS'ine birakilir: modem, sirket ve VPN'in yerel adlari cozulmeye devam eder.
 */
#include <winsock2.h>
#include <windows.h>
#include <winhttp.h>
#include <stdlib.h>
#include <string.h>
#include "windivert.h"

#define DNS_PRIORITY 1000           /* WinDivert 2.x: buyuk deger once; GoodbyeDPI 0'da */
#define MAX_DNS 4096
#define MAX_JOBS 64
#define PKT_MAX 2048                 /* filtre UDP yukunu <= 512 ile sinirlar */

/* Bir dns_start..dns_stop donemi. Isler kendi baglamini tutar: durdurulup yeniden
 * baslatilsa da eski isler eski (kapanmamis) tutamaci kullanir; son biten kapatir. */
typedef struct {
    HANDLE h;
    volatile LONG refs;
    volatile LONG running;
} dns_ctx_t;

typedef struct {
    dns_ctx_t *ctx;
    UINT8 pkt[PKT_MAX];
    UINT len;
    WINDIVERT_ADDRESS addr;
} dns_job_t;

static dns_ctx_t *dns_cur = NULL;
static HANDLE dns_thread = NULL;
static HINTERNET dns_session = NULL;
static volatile LONG dns_jobs = 0;
static volatile LONG dns_good = 0;          /* son calisan sunucu */
static volatile LONG dns_fails = 0;         /* ust uste basarisiz sorgu */
static volatile LONG64 dns_down_until = 0;  /* GetTickCount64: o zamana kadar DoH atlanir (0 = acik) */
static const wchar_t *dns_servers[] = { L"1.1.1.1", L"1.0.0.1", L"8.8.8.8" };

static void ctx_release(dns_ctx_t *c)
{
    if (InterlockedDecrement(&c->refs) == 0) {
        WinDivertClose(c->h);
        free(c);
    }
}

static int dns_open_session(void)
{
    if (dns_session) return 0;
    HINTERNET s = WinHttpOpen(L"SwarlexEasyDpi", WINHTTP_ACCESS_TYPE_NO_PROXY, WINHTTP_NO_PROXY_NAME, WINHTTP_NO_PROXY_BYPASS, 0);
    if (!s) return (int)GetLastError();
    /* ad cozme yok (IP), baglanti/gonderme 1.5 sn, alma 2 sn: bir sunucu en kotu ~5 sn */
    WinHttpSetTimeouts(s, 0, 1500, 1500, 2000);
    if (InterlockedCompareExchangePointer((PVOID *)&dns_session, s, NULL) != NULL) WinHttpCloseHandle(s);
    return 0;
}

/* 64 bit sayac: 49,7 gunde tasan GetTickCount ile ~25 gun sonra DoH sessizce kapaniyordu */
static int doh_down(void) { LONG64 u = dns_down_until; return u != 0 && (LONG64)GetTickCount64() < u; }

static int is_standard_query(const UINT8 *q, UINT len)
{
    return len >= 12 && (q[2] & 0x80) == 0 && ((q[2] >> 3) & 0x0F) == 0;
}

/* Ham DNS sorgusunu DoH ile sorar; cevap uzunlugu ya da -1. En fazla iki sunucu denenir.
 * -2: DoH cevap verdi ama yerel olabilecek bir ad (NXDOMAIN/REFUSED/SERVFAIL): sistem DNS'i denesin. */
static int doh_query(const UINT8 *q, int qlen, UINT8 *out, int cap)
{
    for (int k = 0; k < 2; k++) {
        int i = (int)((dns_good + k) % 3);
        int n = -1;
        HINTERNET c = WinHttpConnect(dns_session, dns_servers[i], INTERNET_DEFAULT_HTTPS_PORT, 0);
        if (!c) continue;
        HINTERNET r = WinHttpOpenRequest(c, L"POST", L"/dns-query", NULL, WINHTTP_NO_REFERER,
                                         WINHTTP_DEFAULT_ACCEPT_TYPES, WINHTTP_FLAG_SECURE);
        if (r) {
            static const wchar_t hdr[] = L"Content-Type: application/dns-message\r\nAccept: application/dns-message\r\n";
            if (WinHttpSendRequest(r, hdr, (DWORD)-1L, (LPVOID)q, (DWORD)qlen, (DWORD)qlen, 0) &&
                WinHttpReceiveResponse(r, NULL)) {
                DWORD status = 0, sz = sizeof(status);
                WinHttpQueryHeaders(r, WINHTTP_QUERY_STATUS_CODE | WINHTTP_QUERY_FLAG_NUMBER,
                                    WINHTTP_HEADER_NAME_BY_INDEX, &status, &sz, WINHTTP_NO_HEADER_INDEX);
                if (status == 200) {
                    DWORD got = 0; int total = 0, more = 0, broken = 0;
                    for (;;) {
                        if (!WinHttpReadData(r, out + total, (DWORD)(cap - total), &got)) { broken = 1; break; }
                        if (got == 0) break;   /* duzgun son */
                        total += (int)got;
                        if (total >= cap) { UINT8 x; DWORD g2 = 0; more = WinHttpReadData(r, &x, 1, &g2) && g2 > 0; break; }
                    }
                    /* gecerli: tam (kesilmemis, yarida kopmamis), en az baslik, ve sorgunun kimligi */
                    if (!more && !broken && total >= 12 && out[0] == q[0] && out[1] == q[1]) n = total;
                }
            }
            WinHttpCloseHandle(r);
        }
        WinHttpCloseHandle(c);
        if (n > 0) {
            InterlockedExchange(&dns_good, i);
            InterlockedExchange(&dns_fails, 0);
            int rcode = out[3] & 0x0F;
            if (rcode == 2 || rcode == 3 || rcode == 5) return -2;
            return n;
        }
    }
    /* ust uste basarisizlik: DoH'u bir sure atla, sorgular sistem DNS'ine beklemeden gitsin */
    if (InterlockedIncrement(&dns_fails) >= 3) {
        InterlockedExchange64(&dns_down_until, (LONG64)GetTickCount64() + 30000);
        InterlockedExchange(&dns_fails, 0);
    }
    return -1;
}

/* Sorgu paketinden, kaynagina gidecek cevap paketini kurar (IPv4 ya da IPv6). */
static UINT build_reply(const dns_job_t *j, const UINT8 *ans, int alen, UINT8 *out, UINT cap)
{
    PWINDIVERT_IPHDR ip = NULL; PWINDIVERT_IPV6HDR ip6 = NULL; PWINDIVERT_UDPHDR udp = NULL;
    if (!WinDivertHelperParsePacket(j->pkt, j->len, &ip, &ip6, NULL, NULL, NULL, NULL, &udp, NULL, NULL, NULL, NULL))
        return 0;
    if (!udp || (!ip && !ip6)) return 0;
    UINT hl = ip ? 20u : 40u, total = hl + 8u + (UINT)alen;
    if (total > cap || total > 0xFFFF) return 0;
    memset(out, 0, hl + 8);
    if (ip) {
        PWINDIVERT_IPHDR o = (PWINDIVERT_IPHDR)out;
        o->Version = 4; o->HdrLength = 5;
        o->Length = htons((UINT16)total);
        o->Id = ip->Id;
        o->TTL = 64; o->Protocol = IPPROTO_UDP;
        o->SrcAddr = ip->DstAddr; o->DstAddr = ip->SrcAddr;
    } else {
        PWINDIVERT_IPV6HDR o = (PWINDIVERT_IPV6HDR)out;
        o->Version = 6;
        o->Length = htons((UINT16)(8 + alen));
        o->NextHdr = IPPROTO_UDP; o->HopLimit = 64;
        memcpy(o->SrcAddr, ip6->DstAddr, sizeof(o->SrcAddr));
        memcpy(o->DstAddr, ip6->SrcAddr, sizeof(o->DstAddr));
    }
    PWINDIVERT_UDPHDR u = (PWINDIVERT_UDPHDR)(out + hl);
    u->SrcPort = udp->DstPort; u->DstPort = udp->SrcPort;
    u->Length = htons((UINT16)(8 + alen));
    memcpy(out + hl + 8, ans, (size_t)alen);
    return total;
}

static DWORD WINAPI dns_job(LPVOID p)
{
    dns_job_t *j = (dns_job_t *)p;
    dns_ctx_t *c = j->ctx;
    PVOID data = NULL; UINT dlen = 0;
    UINT8 ans[MAX_DNS], reply[MAX_DNS + 64];
    int ok = 0;
    if (c->running && !doh_down() &&
        WinDivertHelperParsePacket(j->pkt, j->len, NULL, NULL, NULL, NULL, NULL, NULL, NULL, &data, &dlen, NULL, NULL) &&
        data && dlen >= 12 && dlen <= 512 && is_standard_query((const UINT8 *)data, dlen)) {
        int n = doh_query((const UINT8 *)data, (int)dlen, ans, sizeof(ans));
        if (n > 0 && c->running) {
            UINT len = build_reply(j, ans, n, reply, sizeof(reply));
            if (len) {
                WINDIVERT_ADDRESS a = j->addr;
                a.Outbound = 0;     /* ayni arayuzden gelen cevap gibi */
                WinDivertHelperCalcChecksums(reply, len, &a, 0);
                ok = WinDivertSend(c->h, reply, len, NULL, &a);
            }
        }
    }
    /* DoH olmadi: sorguyu oldugu gibi birak, sistem DNS'i cevaplasin */
    if (!ok) WinDivertSend(c->h, j->pkt, j->len, NULL, &j->addr);   /* durdurulurken de: sorgu kaybolmasin */
    free(j);
    InterlockedDecrement(&dns_jobs);
    ctx_release(c);
    return 0;
}

static DWORD WINAPI dns_loop(LPVOID p)
{
    dns_ctx_t *c = (dns_ctx_t *)p;
    UINT8 spare[PKT_MAX]; UINT slen; WINDIVERT_ADDRESS sa;
    while (c->running) {
        dns_job_t *j = (dns_job_t *)malloc(sizeof(dns_job_t));
        if (!j) {
            /* bellek yok: bu paketi oldugu gibi gecir, yakalamayi birakma */
            if (WinDivertRecv(c->h, spare, sizeof(spare), &slen, &sa)) WinDivertSend(c->h, spare, slen, NULL, &sa);
            else if (!c->running) break;
            else Sleep(10);
            continue;
        }
        if (!WinDivertRecv(c->h, j->pkt, sizeof(j->pkt), &j->len, &j->addr)) {
            DWORD e = GetLastError();
            free(j);
            if (!c->running || e == ERROR_NO_DATA || e == ERROR_INVALID_HANDLE || e == ERROR_OPERATION_ABORTED) break;
            continue;   /* tek paketlik hata (or. cok buyuk): devam */
        }
        /* cok is var ya da DoH bir sure kapali: beklemeden gecir */
        if (dns_jobs >= MAX_JOBS || doh_down()) { WinDivertSend(c->h, j->pkt, j->len, NULL, &j->addr); free(j); continue; }
        j->ctx = c;
        InterlockedIncrement(&c->refs);
        InterlockedIncrement(&dns_jobs);
        if (!QueueUserWorkItem(dns_job, j, WT_EXECUTELONGFUNCTION)) {
            WinDivertSend(c->h, j->pkt, j->len, NULL, &j->addr);
            free(j);
            InterlockedDecrement(&dns_jobs);
            ctx_release(c);
        }
    }
    /* kapatildi: kuyrukta bekleyen sorgulari oldugu gibi gecir (kaybolmasinlar) */
    UINT8 rest[PKT_MAX]; UINT rlen; WINDIVERT_ADDRESS ra;
    while (WinDivertRecv(c->h, rest, sizeof(rest), &rlen, &ra)) WinDivertSend(c->h, rest, rlen, NULL, &ra);
    ctx_release(c);   /* dongunun referansi */
    return 0;
}

/* Tanilama/test: discord.com A kaydini DoH ile sorar; cevaptaki kayit sayisi ya da -1.
 * Yakalama gerektirmez (yonetici olmadan da calisir). */
__declspec(dllexport) int __cdecl dns_selftest(void)
{
    if (dns_open_session()) return -1;
    static const UINT8 q[] = {
        0x53, 0x45, 0x01, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        7, 'd', 'i', 's', 'c', 'o', 'r', 'd', 3, 'c', 'o', 'm', 0, 0x00, 0x01, 0x00, 0x01 };
    UINT8 ans[MAX_DNS];
    int n = doh_query(q, (int)sizeof(q), ans, sizeof(ans));
    if (n < 12) return -1;
    return (ans[6] << 8) | ans[7];   /* ANCOUNT */
}

__declspec(dllexport) int __cdecl dns_start(void)
{
    if (dns_cur) return 0;
    int e = dns_open_session();
    if (e) return e;
    InterlockedExchange(&dns_fails, 0);          /* onceki ag kesintisinin devre kesicisi tasinmasin */
    InterlockedExchange64(&dns_down_until, 0);
    /* yalniz gercek sorgular (yuk 12..512 bayt); VPN/CGNAT ve baglanti-yerel DNS sunuculari haric */
    HANDLE h = WinDivertOpen(
        "outbound and !loopback and udp.DstPort == 53 and udp.PayloadLength >= 12 and udp.PayloadLength <= 512"
        " and (ip ? (ip.DstAddr < 100.64.0.0 or ip.DstAddr > 100.127.255.255) and"
        " (ip.DstAddr < 169.254.0.0 or ip.DstAddr > 169.254.255.255) : ipv6.DstAddr < fe80:: or ipv6.DstAddr > febf:ffff:ffff:ffff:ffff:ffff:ffff:ffff)",
        WINDIVERT_LAYER_NETWORK, DNS_PRIORITY, 0);
    if (h == INVALID_HANDLE_VALUE) return (int)GetLastError();
    dns_ctx_t *c = (dns_ctx_t *)calloc(1, sizeof(dns_ctx_t));
    if (!c) { WinDivertClose(h); return ERROR_OUTOFMEMORY; }
    c->h = h; c->refs = 2;   /* dongu + dns_cur */
    c->running = 1;
    dns_thread = CreateThread(NULL, 0, dns_loop, c, 0, NULL);
    if (!dns_thread) { e = (int)GetLastError(); WinDivertClose(h); free(c); return e; }
    dns_cur = c;
    return 0;
}

/* Beklemeden durdurur: yeni sorgu yakalanmaz; suren isler biter ve son biten tutamaci kapatir. */
__declspec(dllexport) void __cdecl dns_stop(void)
{
    dns_ctx_t *c = dns_cur;
    if (!c) return;
    dns_cur = NULL;
    c->running = 0;
    WinDivertShutdown(c->h, WINDIVERT_SHUTDOWN_RECV);   /* Recv'deki dongu cikar; suren isler gonderebilir */
    if (dns_thread) { WaitForSingleObject(dns_thread, 2000); CloseHandle(dns_thread); dns_thread = NULL; }
    /* WinHTTP oturumu surec boyunca acik kalir: suren isler zaman asimiyla (en kotu ~10 sn) biter ve
     * sorguyu sistem DNS'ine birakir; oturumu burada kapatmak eszamanli islerde guvenli degil. */
    ctx_release(c);   /* dns_cur referansi */
}
