/*
 * SwarlexEasyDpi: GoodbyeDPI'yi ayri bir surec (goodbyedpi.exe) yerine uygulamanin icinde,
 * bir is parcaciginda calistirir (gdpi.dll). goodbyedpi.c dogrudan dahil edilir ki static
 * durumuna (filtreler, exiting, filtre metinleri) erisilebilsin.
 *
 *   gdpi_run(argc, argv)  -> engelleyerek calisir; gdpi_stop() cagrilinca doner.
 *   gdpi_stop()           -> WinDivert tutamaclarini kapatir (Recv'deki dongu cikar).
 *
 * goodbyedpi.c'deki exit()/die() cagrilari (hatali secenek, filtre acilamadi...) uygulamayi
 * kapatmasin diye longjmp ile gdpi_run'a geri doner ve hata kodu olarak verilir.
 */

/* goodbyedpi.c'nin kullandigi sistem basliklari once: asagidaki makrolar onlarin bildirimlerini bozmasin */
#include <stdio.h>
#include <stdlib.h>
#include <stdbool.h>
#include <ctype.h>
#include <signal.h>
#include <unistd.h>
#include <string.h>
#include <getopt.h>
#include <setjmp.h>
#include <in6addr.h>
#include <ws2tcpip.h>
#include "windivert.h"

static jmp_buf gdpi_jump;
static volatile int gdpi_in_run = 0;
static volatile int gdpi_exit_code = 0;
/* Diger kaynak dosyalari da (dnsredir.c, uthash) -Dexit=gdpi_exit ile derlenir. */
void gdpi_exit(int code);
/* Durdurma istendi mi: goodbyedpi.c (GDPI_EMBED) donguye girmeden once kilit altinda bakar. */
int gdpi_should_stop(void);

#define main gdpi_main
/* exit -> gdpi_exit: tum kaynaklar -Dexit=gdpi_exit ile derlenir (build.ps1) */
/* Ctrl+C isleyicisi exit() cagirir; uygulama icinde gereksiz ve tehlikeli */
#define signal(sig, handler) ((void)0)

#include "goodbyedpi.c"

#undef main
#undef signal

/* Hizmet olarak calisma uygulama icinde kullanilmaz (service.c derlenmez). */
int service_register(int argc __attribute__((unused)), char *argv[] __attribute__((unused))) { return 0; }

void gdpi_exit(int code)
{
    if (gdpi_in_run) { gdpi_exit_code = code; longjmp(gdpi_jump, 1); }
    ExitThread((DWORD)code); /* calisma disinda olmamali; yine de sureci degil yalniz is parcacigini bitir */
}

static CRITICAL_SECTION gdpi_lock;
static INIT_ONCE gdpi_once = INIT_ONCE_STATIC_INIT;
static BOOL CALLBACK gdpi_init_lock(PINIT_ONCE o, PVOID p, PVOID *c) { (void)o; (void)p; (void)c; InitializeCriticalSection(&gdpi_lock); return TRUE; }

/* Calisan filtreleri kapatir. Ayni anda iki kez cagrilsa da tutamac bir kez kapanir. */
static void gdpi_close_filters(void)
{
    EnterCriticalSection(&gdpi_lock);
    for (int i = 0; i < filter_num; i++) {
        if (filters[i] && filters[i] != INVALID_HANDLE_VALUE) {
            WinDivertShutdown(filters[i], WINDIVERT_SHUTDOWN_BOTH);
            WinDivertClose(filters[i]);
        }
        filters[i] = NULL;
    }
    filter_num = 0;
    LeaveCriticalSection(&gdpi_lock);
}

int gdpi_should_stop(void)
{
    InitOnceExecuteOnce(&gdpi_once, gdpi_init_lock, NULL, NULL);
    EnterCriticalSection(&gdpi_lock);
    int s = exiting;
    LeaveCriticalSection(&gdpi_lock);
    return s;
}

/* Her calismadan ONCE (calisma is parcacigi baslamadan) cagrilir: durdurma bayragini sifirlar.
 * gdpi_run icinde sifirlansaydi, baslarken gelen bir durdurma kaybolurdu. */
__declspec(dllexport) void __cdecl gdpi_prepare(void)
{
    InitOnceExecuteOnce(&gdpi_once, gdpi_init_lock, NULL, NULL);
    EnterCriticalSection(&gdpi_lock);
    exiting = 0;
    LeaveCriticalSection(&gdpi_lock);
}

/*
 * Doner: 0 = gdpi_stop ile duzgun durdu, >0 = GoodbyeDPI hata kodu (ERROR_* / EXIT_FAILURE),
 * -1 = beklenmeyen (paket alinamadi ve durdurma istenmedi).
 */
__declspec(dllexport) int __cdecl gdpi_run(int argc, char *argv[])
{
    InitOnceExecuteOnce(&gdpi_once, gdpi_init_lock, NULL, NULL);

    /* her calismada temiz durum: filtre metinleri yeniden kurulur, getopt bastan okur
     * (exiting burada sifirlanmaz: gdpi_prepare'de; arada gelen durdurma gecerli kalir) */
    running_from_service = 1;           /* service_register atlanir */
    free(filter_string); filter_string = NULL;
    free(filter_passive_string); filter_passive_string = NULL;
    for (int i = 0; i < MAX_FILTERS; i++) filters[i] = NULL;
    filter_num = 0;
    optind = 0;                         /* mingw getopt: 0 = tam sifirlama */

    int rc;
    gdpi_exit_code = 0;
    if (setjmp(gdpi_jump) == 0) {
        gdpi_in_run = 1;
        gdpi_main(argc, argv);
        rc = gdpi_should_stop() ? 0 : -1;   /* dongu bitti: durdurma istendiyse duzgun */
    } else {
        rc = gdpi_exit_code;                /* exit(0) da 0: duzgun */
    }
    gdpi_in_run = 0;
    gdpi_close_filters();
    return rc;
}

__declspec(dllexport) void __cdecl gdpi_stop(void)
{
    InitOnceExecuteOnce(&gdpi_once, gdpi_init_lock, NULL, NULL);
    /* Yalniz kapatma sinyali: Recv'de bekleyen dongu hata ile doner, tutamaclari gdpi_run kapatir.
     * Filtreler henuz aciliyorsa: goodbyedpi.c donguye girmeden exiting'e bakar. */
    EnterCriticalSection(&gdpi_lock);
    exiting = 1;
    for (int i = 0; i < filter_num; i++)
        if (filters[i] && filters[i] != INVALID_HANDLE_VALUE)
            WinDivertShutdown(filters[i], WINDIVERT_SHUTDOWN_BOTH);
    LeaveCriticalSection(&gdpi_lock);
}
