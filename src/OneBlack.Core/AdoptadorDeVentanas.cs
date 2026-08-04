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

        // Ahora una COLECCIÓN, no una sola ventana. Clave = HWND de la adoptada.
        private readonly Dictionary<IntPtr, EstadoOriginalVentana> adoptadas
            = new Dictionary<IntPtr, EstadoOriginalVentana>();

        private readonly PersistenciaEstado persistencia = new PersistenciaEstado();

        public IntPtr BuscarNotepad() => FindWindow("Notepad", null);

        /// <summary>
        /// ¿Está esta ventana ya adoptada? Evita adoptar dos veces la misma.
        /// </summary>
        public bool YaEstaAdoptada(IntPtr hwndVentana) => adoptadas.ContainsKey(hwndVentana);

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

            SetWindowLong(hwndVentana, GWL_STYLE, estado.EstilosOriginales);
            SetParent(hwndVentana, estado.PadreOriginal);
            SetWindowPos(hwndVentana, IntPtr.Zero,
                estado.X, estado.Y, estado.Ancho, estado.Alto,
                SWP_NOZORDER | SWP_FRAMECHANGED);

            if (hwndContenedor != IntPtr.Zero)
                InvalidateRect(hwndContenedor, IntPtr.Zero, true);

            // Sacar de la colección y volver a persistir el estado (ya sin esta).
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
            // ToList() para poder modificar el diccionario mientras iteramos.
            foreach (var hwnd in adoptadas.Keys.ToList())
                Devolver(hwnd, hwndContenedor);
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