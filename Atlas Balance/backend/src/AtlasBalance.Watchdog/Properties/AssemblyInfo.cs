using System.Runtime.CompilerServices;

// V-02.06 (F2.7): permitir que un futuro proyecto de tests
// (AtlasBalance.Watchdog.Tests) acceda a miembros internal para
// validar LogScrubber y otros helpers sin tener que exponerlos
// publicamente desde el ensamblado del Watchdog.
[assembly: InternalsVisibleTo("AtlasBalance.Watchdog.Tests")]
