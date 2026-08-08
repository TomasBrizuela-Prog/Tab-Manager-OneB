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

        private const uint WM_ACTIVATE = 0x0006;
        private const int WA_CLICKACTIVE = 2;

        private IntPtr hwndInputAcoplado = IntPtr.Zero;

        // Ahora una COLECCIÓN, no una sola ventana. Clave = HWND de la adoptada.
        private readonly Dictionary<IntPtr, EstadoOriginalVentana> adoptadas
            = new Dictionary<IntPtr, EstadoOriginalVentana>();

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

        public bool Adoptar(IntPtr hwndVentana, IntPtr hwndContenedor, int ancho, int alto)
        {
            if (hwndVentana == IntPtr.Zero || hwndContenedor == IntPtr.Zero)
                return false;

            // Guarda contra doble adopción: si ya la tenemos, no la re-procesamos.
            if (adoptadas.ContainsKey(hwndVentana))
                return false;

            // 1. GUARDAR ANTES DE TOCAR.
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

            // 5. REGISTRAR en la colección y PERSISTIR el estado completo.
            adoptadas[hwndVentana] = estado;
            PersistirTodo();

            return true;
        }

        public bool Devolver(IntPtr hwndVentana, IntPtr hwndContenedor)
        {
            if (!adoptadas.TryGetValue(hwndVentana, out var estado))
                return false;

            // IMPORTANTE: asegurar que la ventana esté VISIBLE antes de devolverla.
            // Si estaba oculta por MostrarSolo (SW_HIDE), sin esto quedaría devuelta
            // pero invisible, y el usuario no la puede recuperar.
            ShowWindow(hwndVentana, SW_SHOW);

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
        /// Devuelve TODAS las ventanas adoptadas. Útil para el cierre limpio
        /// de OneBlack (soltar todo antes de salir).
        /// </summary>
        public void DevolverTodas(IntPtr hwndContenedor)
        {
            // Primero, asegurar que TODAS estén visibles (algunas pueden estar ocultas
            // por MostrarSolo). Una ventana oculta no se devuelve bien.
            foreach (var hwnd in adoptadas.Keys.ToList())
                ShowWindow(hwnd, SW_SHOW);

            // Ahora sí devolverlas todas.
            foreach (var hwnd in adoptadas.Keys.ToList())
                Devolver(hwnd, hwndContenedor);
        }
        // Constantes para ShowWindow: ocultar y mostrar.
        private const int SW_HIDE = 0;
        private const int SW_SHOW = 5;

        /// <summary>
        /// Muestra SOLO la ventana indicada y oculta todas las demás adoptadas.
        /// Es la base del cambio de pestaña: una visible, el resto escondidas.
        /// </summary>
        public void MostrarSolo(IntPtr hwndVentana)
        {
            foreach (var hwnd in adoptadas.Keys)
            {
                ShowWindow(hwnd, hwnd == hwndVentana ? SW_SHOW : SW_HIDE);
            }

            // Desacoplar la ventana que estaba acoplada antes (si había otra).
            if (hwndInputAcoplado != IntPtr.Zero && hwndInputAcoplado != hwndVentana)
            {
                AcoplarInput(hwndInputAcoplado, false);
                hwndInputAcoplado = IntPtr.Zero;
            }

            // Acoplar el input a la ventana que ahora se muestra.
            if (hwndVentana != IntPtr.Zero && hwndInputAcoplado != hwndVentana)
            {
                AcoplarInput(hwndVentana, true);
                hwndInputAcoplado = hwndVentana;
            }

            // Despertar el enrutamiento de foco interno de Chromium: VS Code decide
            // a qué vista interna (editor/terminal) mandar el teclado según WM_ACTIVATE.
            // Sin esto, el reparenting rompe ese flujo y el teclado no llega adentro.
            if (hwndVentana != IntPtr.Zero)
            {
                SendMessage(hwndVentana, WM_ACTIVATE, new IntPtr(WA_CLICKACTIVE), IntPtr.Zero);
            }
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
                // Cada adoptada se reencaja al nuevo tamaño, en la esquina (0,0) del hueco.
                MoveWindow(hwnd, 0, 0, ancho, alto, true);
            }
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