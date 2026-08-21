using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace OneBlack.Core
{
    public class EstadoOriginalVentana
    {
        public IntPtr PadreOriginal;
        public int EstilosOriginales;
        public int X, Y, Ancho, Alto;
    }

    /// <summary>
    /// El músculo del reparenting, ahora para VARIAS ventanas a la vez.
    /// Lleva un diccionario de las ventanas adoptadas (clave = HWND) y
    /// persiste el estado en disco tras cada cambio, para el janitor.
    /// </summary>
    public class AdoptadorDeVentanas
    {
        private const int GWL_STYLE = -16;
        private const int WS_CHILD = 0x40000000;
        private const int WS_POPUP = unchecked((int)0x80000000);
        private const int WS_CAPTION = 0x00C00000;
        private const int WS_THICKFRAME = 0x00040000;
        private const uint SWP_NOZORDER = 0x0004;
        private const uint SWP_FRAMECHANGED = 0x0020;

        [DllImport("user32.dll")]
        private static extern bool UpdateWindow(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);
        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll", SetLastError = true)]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
            int X, int Y, int cx, int cy, uint uFlags);
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
        [DllImport("user32.dll")]
        private static extern IntPtr GetDesktopWindow();
        [DllImport("user32.dll")]
        private static extern bool InvalidateRect(IntPtr hWnd, IntPtr lpRect, bool bErase);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern bool MoveWindow(IntPtr hWnd, int X, int Y, int nWidth, int nHeight, bool bRepaint);

        // Chequeo de validez de un HWND. Devuelve false si la ventana ya no existe
        // (la app la destruyó, el usuario mató el proceso, etc.). Es la base de la
        // salvaguarda: nunca operamos Win32 sobre un handle muerto.
        [DllImport("user32.dll")]
        private static extern bool IsWindow(IntPtr hWnd);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }


        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool RedrawWindow(IntPtr hWnd, IntPtr lprcUpdate, IntPtr hrgnUpdate, uint flags);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr SetFocus(IntPtr hWnd);

        // ===== Subclasificación (para clavar la ventana adoptada) =====
        // SetWindowSubclass inserta nuestra función en la cadena de mensajes de la
        // ventana; DefSubclassProc pasa al siguiente en la cadena; RemoveWindowSubclass
        // la quita. Viven en comctl32.dll. Es la vía SEGURA de subclasificar (a
        // diferencia del viejo SetWindowLongPtr, que es frágil entre procesos).
        [DllImport("comctl32.dll", SetLastError = true)]
        private static extern bool SetWindowSubclass(IntPtr hWnd, SubclassProc pfnSubclass,
            IntPtr uIdSubclass, IntPtr dwRefData);

        [DllImport("comctl32.dll", SetLastError = true)]
        private static extern bool RemoveWindowSubclass(IntPtr hWnd, SubclassProc pfnSubclass,
            IntPtr uIdSubclass);

        [DllImport("comctl32.dll")]
        private static extern IntPtr DefSubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);

        // Firma de la función de subclase que Windows llamará por cada mensaje.
        private delegate IntPtr SubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam,
            IntPtr lParam, IntPtr uIdSubclass, IntPtr dwRefData);

        // La estructura que viaja en WM_WINDOWPOSCHANGING: describe el cambio de
        // posición/tamaño que Windows ESTÁ POR aplicar. Si le encendemos los flags
        // NOMOVE/NOSIZE, Windows descarta el cambio → la ventana queda clavada.
        [StructLayout(LayoutKind.Sequential)]
        private struct WINDOWPOS
        {
            public IntPtr hwnd;
            public IntPtr hwndInsertAfter;
            public int x, y, cx, cy;
            public uint flags;
        }

        private const uint WM_WINDOWPOSCHANGING = 0x0046;
        private const uint SWP_NOSIZE = 0x0001;
        // (SWP_NOMOVE ya está declarado más abajo.)

        // ===== Fin subclasificación =====

        // Flags de SetWindowPos que faltaban para el latigazo (no mover, no activar).
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOACTIVATE = 0x0010;

        // Flags de RedrawWindow: invalidar + repintar YA + alcanzar ventanas hijas.
        // RDW_ALLCHILDREN es clave: llega a las ventanas internas de Chromium, no solo
        // a la top-level. Por eso es más contundente que InvalidateRect.
        private const uint RDW_INVALIDATE = 0x0001;
        private const uint RDW_UPDATENOW = 0x0100;
        private const uint RDW_ALLCHILDREN = 0x0080;

        private const uint WM_ACTIVATE = 0x0006;
        private const int WA_CLICKACTIVE = 2;

        private IntPtr hwndInputAcoplado = IntPtr.Zero;

        // Ahora una COLECCIÓN, no una sola ventana. Clave = HWND de la adoptada.
        private readonly Dictionary<IntPtr, EstadoOriginalVentana> adoptadas
            = new Dictionary<IntPtr, EstadoOriginalVentana>();

        // Guardamos el delegate de subclase POR VENTANA. Es OBLIGATORIO conservar la
        // referencia viva: si el GC recolecta el delegate mientras Windows aún lo usa,
        // el proceso crashea. Mismo motivo por el que VentanaAnfitriona guarda su wndProc.
        private readonly Dictionary<IntPtr, SubclassProc> subclases
            = new Dictionary<IntPtr, SubclassProc>();

        private readonly PersistenciaEstado persistencia = new PersistenciaEstado();


        /// <summary>
        /// ¿Está esta ventana ya adoptada? Evita adoptar dos veces la misma.
        /// </summary>
        public bool YaEstaAdoptada(IntPtr hwndVentana) => adoptadas.ContainsKey(hwndVentana);

        /// <summary>
        /// Los HWND de todas las ventanas actualmente adoptadas.
        /// Útil para que la UI no las pierda al refrescar la lista de candidatas.
        /// </summary>
        public IEnumerable<IntPtr> HwndsAdoptados() => adoptadas.Keys.ToList();

        /// <summary>
        /// Reaplica SOLO el foco de teclado a una ventana ya adoptada y visible, sin tocar
        /// show/hide, encaje ni repintado. Es la versión quirúrgica para recuperar el teclado
        /// tras algo que robó el foco (una notificación del IDE), sin los efectos secundarios
        /// de MostrarSolo (que reencaja y repinta, y por eso no sirve para disparar seguido).
        /// </summary>
        public void ReaplicarFoco(IntPtr hwndVentana)
        {
            if (hwndVentana == IntPtr.Zero || !IsWindow(hwndVentana))
                return;
            if (!adoptadas.ContainsKey(hwndVentana))
                return;

            // Asegurar el acople de input a esta ventana (si no estaba acoplada).
            if (hwndInputAcoplado != hwndVentana)
            {
                // Desacoplar la anterior, si había otra.
                if (hwndInputAcoplado != IntPtr.Zero)
                    AcoplarInput(hwndInputAcoplado, false);

                AcoplarInput(hwndVentana, true);
                hwndInputAcoplado = hwndVentana;
            }

            // Los tres pasos que entregan el foco (mismos que MostrarSolo, sin lo demás).
            SendMessage(hwndVentana, WM_ACTIVATE, new IntPtr(WA_CLICKACTIVE), IntPtr.Zero);
            SetForegroundWindow(hwndVentana);
            SetFocus(hwndVentana);
        }
        public bool Adoptar(IntPtr hwndVentana, IntPtr hwndContenedor, int ancho, int alto)
        {
            if (hwndVentana == IntPtr.Zero || hwndContenedor == IntPtr.Zero)
                return false;

            // Salvaguarda: no adoptar una ventana que ya no existe (la candidata
            // pudo haberse cerrado entre que se enumeró y que se tocó Adoptar).
            if (!IsWindow(hwndVentana))
                return false;

            // Guarda contra doble adopción: si ya la tenemos, no la re-procesamos.
            if (adoptadas.ContainsKey(hwndVentana))
                return false;

            // 1. GUARDAR ANTES DE TOCAR. Guardamos los estilos EXACTOS, sin tocar
            //    ningún bit: la regla de oro es restaurar idéntico a como estaba.
            GetWindowRect(hwndVentana, out RECT rect);
            var estado = new EstadoOriginalVentana
            {
                PadreOriginal = GetDesktopWindow(),
                EstilosOriginales = GetWindowLong(hwndVentana, GWL_STYLE),
                X = rect.Left,
                Y = rect.Top,
                Ancho = rect.Right - rect.Left,
                Alto = rect.Bottom - rect.Top
            };

            // 2. CAMBIAR ESTILOS.
            int estilos = estado.EstilosOriginales;
            estilos &= ~WS_POPUP;
            estilos &= ~WS_CAPTION;
            estilos &= ~WS_THICKFRAME;
            estilos |= WS_CHILD;
            SetWindowLong(hwndVentana, GWL_STYLE, estilos);

            // 3. REPARENTING.
            SetParent(hwndVentana, hwndContenedor);

            // 4. ENCAJAR.
            SetWindowPos(hwndVentana, IntPtr.Zero, 0, 0, ancho, alto,
                SWP_NOZORDER | SWP_FRAMECHANGED);

            // 5. CLAVAR: subclasificar para que la ventana no pueda moverse ni
            //    redimensionarse por su cuenta (ni el usuario, ni VS Code al maximizar
            //    o notificar). Solo ReajustarTamaño podrá reposicionarla.
            ClavarVentana(hwndVentana);

            // 6. REGISTRAR en la colección y PERSISTIR el estado completo.
            adoptadas[hwndVentana] = estado;
            PersistirTodo();

            return true;
        }

        public bool Devolver(IntPtr hwndVentana, IntPtr hwndContenedor)
        {
            if (!adoptadas.TryGetValue(hwndVentana, out var estado))
                return false;

            // SALVAGUARDA: si la ventana ya no existe (la app la destruyó mientras
            // estaba adoptada —caso WinUI que recrea sus ventanas—, o el usuario mató
            // el proceso), no hay handle vivo sobre el cual operar. Aplicar Win32 sobre
            // un handle muerto deja basura y estado inconsistente. En vez de eso,
            // limpiamos nuestro estado interno con dignidad y salimos.
            if (!IsWindow(hwndVentana))
            {
                LimpiarAdoptada(hwndVentana);
                return false;
            }

            // DESCLAVAR: quitar la subclase ANTES de restaurar. Una subclase colgada
            // tras devolver dejaría nuestro código corriendo sobre una ventana que ya
            // no gestionamos (y bloqueándole el movimiento al usuario legítimo).
            DesclavarVentana(hwndVentana);

            // IMPORTANTE: asegurar que la ventana esté VISIBLE antes de devolverla.
            // Si estaba oculta por MostrarSolo (SW_HIDE), sin esto quedaría devuelta
            // pero invisible, y el usuario no la puede recuperar.
            ShowWindow(hwndVentana, SW_SHOW);

            // Restauramos EXACTAMENTE los estilos guardados (regla de oro: idéntico).
            SetWindowLong(hwndVentana, GWL_STYLE, estado.EstilosOriginales);
            SetParent(hwndVentana, estado.PadreOriginal);
            SetWindowPos(hwndVentana, IntPtr.Zero,
                estado.X, estado.Y, estado.Ancho, estado.Alto,
                SWP_NOZORDER | SWP_FRAMECHANGED);

            if (hwndContenedor != IntPtr.Zero)
            {
                InvalidateRect(hwndContenedor, IntPtr.Zero, true);
                UpdateWindow(hwndContenedor);
            }

            if (hwndInputAcoplado == hwndVentana)
            {
                AcoplarInput(hwndVentana, false);
                hwndInputAcoplado = IntPtr.Zero;
            }
            adoptadas.Remove(hwndVentana);
            PersistirTodo();
            return true;
        }

        /// <summary>
        /// Limpia el estado interno de una ventana adoptada que ya no se puede operar
        /// (handle muerto). No hace Win32 sobre el handle: solo suelta nuestras
        /// referencias y re-persiste. Así OneBlack no queda con entradas fantasma.
        /// </summary>
        private void LimpiarAdoptada(IntPtr hwndVentana)
        {
            // Si teníamos el input acoplado a esta ventana muerta, soltamos la
            // referencia sin intentar AttachThreadInput(false): el thread de la
            // ventana puede ya no existir y la llamada no tendría sentido.
            if (hwndInputAcoplado == hwndVentana)
                hwndInputAcoplado = IntPtr.Zero;

            // Soltar el delegate de subclase (no llamamos RemoveWindowSubclass sobre
            // un handle muerto: no tiene sentido y podría fallar). Basta con soltar
            // nuestra referencia para que el GC lo recolecte.
            subclases.Remove(hwndVentana);

            adoptadas.Remove(hwndVentana);
            PersistirTodo();
        }

        /// <summary>
        /// Devuelve TODAS las ventanas adoptadas. Útil para el cierre limpio
        /// de OneBlack (soltar todo antes de salir).
        /// </summary>
        public void DevolverTodas(IntPtr hwndContenedor)
        {
            // Primero, asegurar que TODAS las vivas estén visibles (algunas pueden
            // estar ocultas por MostrarSolo). Una ventana oculta no se devuelve bien.
            foreach (var hwnd in adoptadas.Keys.ToList())
                if (IsWindow(hwnd))
                    ShowWindow(hwnd, SW_SHOW);

            // Ahora sí devolverlas todas. Devolver ya maneja internamente el caso
            // de un handle muerto (limpia sin operar), así que es seguro llamarlo
            // para todas sin chequear acá.
            foreach (var hwnd in adoptadas.Keys.ToList())
                Devolver(hwnd, hwndContenedor);
        }
        // Constantes para ShowWindow: ocultar y mostrar.
        private const int SW_HIDE = 0;
        private const int SW_SHOW = 5;

        /// <summary>
        /// Muestra SOLO la ventana indicada y oculta todas las demás adoptadas.
        /// Es la base del cambio de pestaña: una visible, el resto escondidas.
        /// Además refuerza el foco de teclado, necesario sobre todo cuando OneBlack
        /// lanzó la ventana él mismo (nace sin haber sido activada por el usuario).
        /// </summary>
        public void MostrarSolo(IntPtr hwndVentana)
        {
            foreach (var hwnd in adoptadas.Keys)
            {
                if (!IsWindow(hwnd))
                    continue;

                ShowWindow(hwnd, hwnd == hwndVentana ? SW_SHOW : SW_HIDE);
            }

            // Desacoplar la ventana que estaba acoplada antes (si había otra).
            if (hwndInputAcoplado != IntPtr.Zero && hwndInputAcoplado != hwndVentana)
            {
                AcoplarInput(hwndInputAcoplado, false);
                hwndInputAcoplado = IntPtr.Zero;
            }

            if (hwndVentana == IntPtr.Zero || !IsWindow(hwndVentana))
                return;

            // Acoplar el input a la ventana que ahora se muestra.
            if (hwndInputAcoplado != hwndVentana)
            {
                AcoplarInput(hwndVentana, true);
                hwndInputAcoplado = hwndVentana;
            }

            // REFUERZO DE FOCO (clave para ventanas lanzadas por OneBlack):
            // Una ventana que OneBlack lanzó nunca fue "activada" por el usuario, así que
            // el SO no la considera foreground y el teclado no llega, por más que las colas
            // de input estén acopladas. Estos tres pasos la activan de verdad:
            //   1. WM_ACTIVATE  → despierta el enrutamiento de foco interno de Chromium.
            //   2. SetForegroundWindow → la marca como la ventana activa a nivel SO.
            //   3. SetFocus     → le da el foco de teclado explícito (con el input ya acoplado,
            //                     ahora sí "prende" sobre la ventana correcta).
            SendMessage(hwndVentana, WM_ACTIVATE, new IntPtr(WA_CLICKACTIVE), IntPtr.Zero);
            SetForegroundWindow(hwndVentana);
            SetFocus(hwndVentana);

            // Latigazo de repintado al mostrar (Chromium no repinta ante SW_SHOW/WM_ACTIVATE).
            ForzarRepintado(hwndVentana);
        }

        /// <summary>
        /// Acopla (o desacopla) la cola de input de OneBlack con la de la ventana
        /// adoptada, para que el teclado fluya a donde el IDE dirija su foco interno
        /// (editor, terminal, etc.). fAttach=true al adoptar, false al devolver.
        /// Desacoplar SIEMPRE al devolver: un acople colgado rompe el foco del sistema.
        /// </summary>
        private void AcoplarInput(IntPtr hwndVentana, bool acoplar)
        {
            uint threadOneBlack = GetCurrentThreadId();
            uint threadVentana = GetWindowThreadProcessId(hwndVentana, out _);

            if (threadOneBlack == threadVentana)
                return;

            // Acoplamos el thread de la ventana AL de OneBlack (orden invertido
            // respecto al intento anterior). La dirección importa en AttachThreadInput.
            AttachThreadInput(threadVentana, threadOneBlack, acoplar);
        }

        /// <summary>
        /// Reajusta el tamaño de todas las ventanas adoptadas al nuevo tamaño del
        /// contenedor. Se llama cuando el hueco (la VentanaAnfitriona) cambia de tamaño.
        /// </summary>
        public void ReajustarTamaño(int ancho, int alto)
        {
            foreach (var hwnd in adoptadas.Keys)
            {
                // Saltear handles muertos: MoveWindow sobre uno no tiene efecto.
                if (!IsWindow(hwnd))
                    continue;

                // Cada adoptada se reencaja al nuevo tamaño, en la esquina (0,0) del hueco.
                // NOTA: este MoveWindow es NUESTRO reposicionamiento legítimo. La subclase
                // que clava la ventana solo bloquea WM_WINDOWPOSCHANGING; MoveWindow pasa
                // por otra vía y sí reposiciona. Por eso el encaje sigue funcionando aunque
                // la ventana esté "clavada" contra movimientos externos.
                MoveWindow(hwnd, 0, 0, ancho, alto, true);
            }
        }

        /// <summary>
        /// Salvaguarda de dibujado. Fuerza a una ventana adoptada a repintar su contenido.
        /// Existe porque VS Code (Chromium) —y ocasionalmente cualquier ventana bajo carga—
        /// a veces NO repinta tras un adoptar/mostrar rápido y queda en negro con el frame viejo.
        ///
        /// Técnica: un "latigazo" de tamaño (encoger 1px y volver). El WM_SIZE resultante
        /// obliga a Chromium a recomponer su superficie de render —cosa que ni SW_SHOW ni
        /// WM_ACTIVATE logran—. Es imperceptible (1px, sin mover, sin activar, sin tocar Z).
        ///
        /// Es race-safe A PROPÓSITO: si la ventana ya fue devuelta (p.ej. un reintento
        /// diferido que llegó tarde), no hace nada. Así los reintentos son inofensivos y
        /// nunca corren SetWindowPos sobre una ventana ya restaurada.
        /// </summary>
        public void ForzarRepintado(IntPtr hwndVentana)
        {
            // Si ya no la gestionamos, ignorar. Esto es lo que hace seguros los reintentos.
            if (!adoptadas.ContainsKey(hwndVentana))
                return;

            // Salvaguarda extra: si el handle murió, no operamos sobre él.
            if (!IsWindow(hwndVentana))
                return;

            // La adoptada ocupa todo el hueco: tomamos su tamaño actual como referencia.
            GetWindowRect(hwndVentana, out RECT r);
            int ancho = r.Right - r.Left;
            int alto = r.Bottom - r.Top;
            if (ancho <= 1 || alto <= 1)
                return;

            // Latigazo: -1px y volver. Dispara el WM_SIZE que despierta el render.
            SetWindowPos(hwndVentana, IntPtr.Zero, 0, 0, ancho - 1, alto,
                SWP_NOMOVE | SWP_NOZORDER | SWP_NOACTIVATE);
            SetWindowPos(hwndVentana, IntPtr.Zero, 0, 0, ancho, alto,
                SWP_NOMOVE | SWP_NOZORDER | SWP_NOACTIVATE);

            // Repintado inmediato, alcanzando también las ventanas hijas internas de Chromium.
            RedrawWindow(hwndVentana, IntPtr.Zero, IntPtr.Zero,
                RDW_INVALIDATE | RDW_UPDATENOW | RDW_ALLCHILDREN);
        }

        /// <summary>
        /// CLAVA una ventana adoptada: la subclasifica para interceptar sus intentos de
        /// moverse/redimensionarse y anularlos. Tras esto, la ventana no puede cambiar de
        /// posición ni tamaño por su cuenta (ni el usuario arrastrándola, ni VS Code al
        /// maximizar o mostrar una notificación). Solo ReajustarTamaño (vía MoveWindow)
        /// puede reposicionarla, porque MoveWindow no pasa por el mensaje que bloqueamos.
        /// </summary>
        private void ClavarVentana(IntPtr hwndVentana)
        {
            // Creamos el delegate y lo GUARDAMOS en el diccionario para que el GC no lo
            // recolecte mientras Windows lo tenga registrado (crash clásico si se omite).
            SubclassProc proc = SubclaseClavado;
            subclases[hwndVentana] = proc;
            SetWindowSubclass(hwndVentana, proc, IntPtr.Zero, IntPtr.Zero);
        }

        /// <summary>
        /// DESCLAVA una ventana: quita la subclase y suelta el delegate. Se llama en
        /// Devolver, ANTES de restaurar, para que la ventana recupere su libertad de
        /// movimiento normal al volver al escritorio.
        /// </summary>
        private void DesclavarVentana(IntPtr hwndVentana)
        {
            if (subclases.TryGetValue(hwndVentana, out var proc))
            {
                RemoveWindowSubclass(hwndVentana, proc, IntPtr.Zero);
                subclases.Remove(hwndVentana);
            }
        }

        /// <summary>
        /// La función de subclase que Windows llama por cada mensaje de la ventana
        /// adoptada. Solo nos interesa WM_WINDOWPOSCHANGING: cuando la ventana está por
        /// moverse o redimensionarse, encendemos SWP_NOMOVE|SWP_NOSIZE en la estructura
        /// WINDOWPOS, y Windows descarta ese cambio → la ventana queda clavada.
        /// Todos los demás mensajes pasan intactos a la cadena original (DefSubclassProc).
        /// </summary>
        private IntPtr SubclaseClavado(IntPtr hWnd, uint uMsg, IntPtr wParam,
            IntPtr lParam, IntPtr uIdSubclass, IntPtr dwRefData)
        {
            if (uMsg == WM_WINDOWPOSCHANGING)
            {
                // lParam apunta a una estructura WINDOWPOS. La leemos, le encendemos los
                // flags que anulan movimiento y tamaño, y la reescribimos en memoria.
                var pos = Marshal.PtrToStructure<WINDOWPOS>(lParam);
                pos.flags |= SWP_NOMOVE | SWP_NOSIZE;
                Marshal.StructureToPtr(pos, lParam, false);
            }

            // El resto sigue su curso normal por la cadena de mensajes.
            return DefSubclassProc(hWnd, uMsg, wParam, lParam);
        }

        /// <summary>
        /// Vuelca el estado completo de la colección al archivo, de forma atómica.
        /// Se llama tras cada adopción y cada devolución.
        /// </summary>
        private void PersistirTodo()
        {
            if (adoptadas.Count == 0)
            {
                // No queda nada adoptado: cierre limpio, borrar el archivo.
                persistencia.Borrar();
                return;
            }

            var lista = adoptadas.Select(par => new VentanaPersistida
            {
                Hwnd = par.Key.ToInt64(),
                PadreOriginal = par.Value.PadreOriginal.ToInt64(),
                EstilosOriginales = par.Value.EstilosOriginales,
                X = par.Value.X,
                Y = par.Value.Y,
                Ancho = par.Value.Ancho,
                Alto = par.Value.Alto
            }).ToList();

            persistencia.Guardar(lista);
        }
    }
}