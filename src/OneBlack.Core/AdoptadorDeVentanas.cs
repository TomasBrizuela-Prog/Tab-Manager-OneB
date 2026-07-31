using System;
using System.Runtime.InteropServices;

namespace OneBlack.Core
{
    /// <summary>
    /// Guarda el estado original de una ventana antes de adoptarla,
    /// para poder devolverla intacta. La regla de oro del reparenting:
    /// guardar ANTES de tocar, restaurar al soltar.
    /// </summary>
    public class EstadoOriginalVentana
    {
        public IntPtr PadreOriginal;   // quién era el padre antes (normalmente el escritorio)
        public int EstilosOriginales;  // los estilos de ventana que tenía
        public int X, Y, Ancho, Alto;  // dónde y de qué tamaño estaba
    }

    /// <summary>
    /// El músculo del reparenting: encuentra una ventana externa ( Ej: Notepad),
    /// la mete dentro de un HWND contenedor, y sabe devolverla a su estado original.
    /// </summary>
    public class AdoptadorDeVentanas
    {
        // --- Constantes Win32 ---
        private const int GWL_STYLE = -16;
        private const int WS_CHILD = 0x40000000;
        private const int WS_POPUP = unchecked((int)0x80000000);
        private const int WS_CAPTION = 0x00C00000;        // barra de título
        private const int WS_THICKFRAME = 0x00040000;     // borde redimensionable
        private const uint SWP_NOZORDER = 0x0004;
        private const uint SWP_FRAMECHANGED = 0x0020;

        // --- P/Invoke ---
        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

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

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }

        // Estado guardado de la ventana que adoptamos (para devolverla).
        private EstadoOriginalVentana estadoGuardado;
        private IntPtr hwndAdoptada = IntPtr.Zero;

        /// <summary>
        /// Busca la ventana de Notepad por su nombre de clase.
        /// Devuelve IntPtr.Zero si no la encuentra.
        /// </summary>
        public IntPtr BuscarNotepad()
        {
            // "Notepad" es el nombre de clase de la ventana del Bloc de notas clásico.
            return FindWindow("Notepad", null);
        }

        /// <summary>
        /// Adopta la ventana: guarda su estado, la vuelve hija del contenedor,
        /// le saca los adornos de ventana suelta, y la encaja en la región dada.
        /// </summary>
        public bool Adoptar(IntPtr hwndVentana, IntPtr hwndContenedor, int ancho, int alto)
        {
            if (hwndVentana == IntPtr.Zero || hwndContenedor == IntPtr.Zero)
                return false;

            // 1. GUARDAR ANTES DE TOCAR. Sin esto no podemos devolverla sana.
            GetWindowRect(hwndVentana, out RECT rect);
            estadoGuardado = new EstadoOriginalVentana
            {
                PadreOriginal = GetDesktopWindow(),   // volverá al escritorio al soltar
                EstilosOriginales = GetWindowLong(hwndVentana, GWL_STYLE),
                X = rect.Left,
                Y = rect.Top,
                Ancho = rect.Right - rect.Left,
                Alto = rect.Bottom - rect.Top
            };
            hwndAdoptada = hwndVentana;

            // 2. CAMBIAR ESTILOS: sacar los de ventana suelta, poner el de hija.
            int estilos = estadoGuardado.EstilosOriginales;
            estilos &= ~WS_POPUP;        // quitar popup
            estilos &= ~WS_CAPTION;      // quitar barra de título
            estilos &= ~WS_THICKFRAME;   // quitar borde redimensionable
            estilos |= WS_CHILD;         // agregar: es hija
            SetWindowLong(hwndVentana, GWL_STYLE, estilos);

            // 3. EL REPARENTING: el padre de Notepad ahora es nuestro contenedor.
            SetParent(hwndVentana, hwndContenedor);

            // 4. ENCAJARLA en la región (arriba-izquierda del contenedor, tamaño dado).
            SetWindowPos(hwndVentana, IntPtr.Zero, 0, 0, ancho, alto,
                SWP_NOZORDER | SWP_FRAMECHANGED);

            return true;
        }

        /// <summary>
        /// Devuelve la ventana adoptada a su estado original: la saca del contenedor,
        /// le restaura estilos, padre y posición. La regla de oro cerrada.
        /// </summary>
        public bool Devolver(IntPtr hwndContenedor)
        {
            if (hwndAdoptada == IntPtr.Zero || estadoGuardado == null)
                return false;

            // Restaurar en orden inverso al que adoptamos.
            SetWindowLong(hwndAdoptada, GWL_STYLE, estadoGuardado.EstilosOriginales);
            SetParent(hwndAdoptada, estadoGuardado.PadreOriginal);
            SetWindowPos(hwndAdoptada, IntPtr.Zero,
                estadoGuardado.X, estadoGuardado.Y,
                estadoGuardado.Ancho, estadoGuardado.Alto,
                SWP_NOZORDER | SWP_FRAMECHANGED);

            // Limpiar el fantasma: forzar al contenedor a repintarse vacío.
            if (hwndContenedor != IntPtr.Zero)
                InvalidateRect(hwndContenedor, IntPtr.Zero, true);

            hwndAdoptada = IntPtr.Zero;
            estadoGuardado = null;
            return true;
        }
    }
}