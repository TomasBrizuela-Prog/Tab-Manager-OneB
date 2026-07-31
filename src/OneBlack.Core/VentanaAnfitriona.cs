using System;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace OneBlack.Core
{
    /// <summary>
    /// Puente entre el mundo pintado de WPF y una ventana Win32 real.
    /// Hereda de HwndHost: WPF la ubica en su layout, pero por debajo
    /// creamos un HWND genuino del sistema donde después reparentamos
    /// la ventana de un IDE (por ahora, Notepad en el spike).
    /// </summary>
    public class VentanaAnfitriona : HwndHost
    {
        // --- Constantes Win32 ---
        // Estilos de ventana. Son "flags" (bits) que definen cómo se comporta
        // y se ve una ventana. Los combinamos con OR (|).
        private const int WS_CHILD = 0x40000000;      // la ventana es hija de otra
        private const int WS_VISIBLE = 0x10000000;    // arranca visible

        // Índice para GetWindowLong/SetWindowLong: -16 significa "los estilos".
        private const int GWL_STYLE = -16;

        // Nombre de clase de la ventana contenedora que vamos a registrar.
        private const string CLASE_CONTENEDOR = "OneBlackContenedorWin32";

        // HWND del hueco que creamos (nuestra ventana contenedora real).
        private IntPtr hwndContenedor;

        // Guardamos el delegate del window procedure para que el GC no lo
        // recolecte mientras Windows todavía lo usa (bug clásico si se omite).
        private WndProcDelegate wndProcDelegate;

        // --- Declaraciones P/Invoke ---
        // P/Invoke = la forma de .NET de llamar funciones de la API de Windows.
        // Declaramos la firma de cada función nativa que necesitamos.

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr CreateWindowEx(
            int dwExStyle, string lpClassName, string lpWindowName,
            int dwStyle, int x, int y, int nWidth, int nHeight,
            IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyWindow(IntPtr hwnd);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateSolidBrush(int crColor);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern ushort RegisterClassEx(ref WNDCLASSEX lpwcx);

        [DllImport("user32.dll")]
        private static extern IntPtr DefWindowProc(
            IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        // Delegate que representa el "window procedure": la función que
        // atiende los mensajes que Windows le manda a nuestra ventana.
        private delegate IntPtr WndProcDelegate(
            IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        // Estructura que describe una clase de ventana al registrarla.
        [StructLayout(LayoutKind.Sequential)]
        private struct WNDCLASSEX
        {
            public uint cbSize;
            public uint style;
            public WndProcDelegate lpfnWndProc;
            public int cbClsExtra;
            public int cbWndExtra;
            public IntPtr hInstance;
            public IntPtr hIcon;
            public IntPtr hCursor;
            public IntPtr hbrBackground;
            public string lpszMenuName;
            public string lpszClassName;
            public IntPtr hIconSm;
        }

        /// <summary>
        /// WPF llama a este método cuando va a mostrar el HwndHost.
        /// Nuestro trabajo: crear una ventana Win32 real y devolver su HWND.
        /// Ese HWND es el "hueco" donde luego reparentamos Notepad.
        /// </summary>
        protected override HandleRef BuildWindowCore(HandleRef hwndParent)
        {
            RegistrarClaseContenedor();

            // Creamos la ventana contenedora como HIJA del HWND que WPF nos da.
            // Arranca sin tamaño (0,0): WPF la va a redimensionar sola vía layout.
            hwndContenedor = CreateWindowEx(
                0,
                CLASE_CONTENEDOR,
                "",
                WS_CHILD | WS_VISIBLE,
                0, 0, 0, 0,
                hwndParent.Handle,   // padre = el HWND que WPF nos pasa
                IntPtr.Zero,
                GetModuleHandle(null),
                IntPtr.Zero);

            return new HandleRef(this, hwndContenedor);
        }

        /// <summary>
        /// WPF llama a este método cuando el HwndHost se destruye.
        /// Acá va la limpieza. En el futuro, ANTES de destruir, este es
        /// el lugar donde devolveremos el IDE reparentado a su padre original.
        /// </summary>
        protected override void DestroyWindowCore(HandleRef hwnd)
        {
            if (hwndContenedor != IntPtr.Zero)
            {
                DestroyWindow(hwndContenedor);
                hwndContenedor = IntPtr.Zero;
            }
        }

        /// <summary>
        /// Expone el HWND del contenedor para que el resto del core
        /// (el reparenting de Notepad) lo use como nuevo padre.
        /// </summary>
        public IntPtr ObtenerHwndContenedor()
        {
            return hwndContenedor;
        }

        // Registra la "clase de ventana" que Windows necesita antes de crear
        // una ventana de ese tipo. Es un trámite obligatorio de Win32.
        private void RegistrarClaseContenedor()
        {
            wndProcDelegate = new WndProcDelegate(DefWindowProc);

            var wc = new WNDCLASSEX
            {
                cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(),
                lpfnWndProc = wndProcDelegate,
                hInstance = GetModuleHandle(null),
                lpszClassName = CLASE_CONTENEDOR,
                hbrBackground = CreateSolidBrush(0x000B0A0A)   // fondo negro OneBlack
            };

            RegisterClassEx(ref wc);
        }
    }
}