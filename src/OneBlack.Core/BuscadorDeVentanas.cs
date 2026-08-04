using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace OneBlack.Core
{
    /// <summary>
    /// Una ventana candidata a ser adoptada: lo que el usuario podría elegir.
    /// </summary>
    public class VentanaCandidata
    {
        public IntPtr Hwnd;
        public string Titulo = "";
        public string NombreProceso = "";
        public ProgramaSoportado Programa = null!;  // ← agregar: qué programa soportado es

        // Mostramos el nombre lindo en vez del proceso crudo.
        public override string ToString() => $"{Programa.NombreMostrado}: {Titulo}  (HWND {Hwnd})";
    }

    /// <summary>
    /// Recorre TODAS las ventanas del sistema (EnumWindows) y devuelve las
    /// que son candidatas reales a adoptar: visibles y con título.
    /// Reemplaza a FindWindow, que solo servía para casos simples como Notepad.
    /// </summary>
    public class BuscadorDeVentanas
    {
        // --- P/Invoke ---

        // El delegate: la firma de NUESTRA función que Windows llamará una vez
        // por cada ventana. Devuelve true = "seguí enumerando", false = "pará".
        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        // EnumWindows recibe nuestro callback y lo ejecuta por cada ventana top-level.
        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll")]
        private static extern int GetWindowTextLength(IntPtr hWnd);

        // Dado un HWND, obtiene el PID del proceso dueño de esa ventana.
        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        /// <summary>
        /// Enumera todas las ventanas y devuelve las candidatas adoptables.
        /// </summary>
        public List<VentanaCandidata> BuscarCandidatas()
        {
            var candidatas = new List<VentanaCandidata>();

            // IMPORTANTE: guardamos el delegate en una variable local que vive
            // durante toda la llamada a EnumWindows. Como EnumWindows es síncrono
            // (termina antes de que esta función retorne), acá alcanza con la local
            // —el GC no la recolecta mientras la usamos—. Si fuera asíncrono habría
            // que guardarla en un campo, como hicimos con el wndProc.
            EnumWindowsProc callback = (hWnd, lParam) =>
            {
                // --- Este bloque se ejecuta UNA VEZ POR CADA ventana del sistema ---

                // Filtro 1: descartar ventanas invisibles (hay cientos, son ruido).
                if (!IsWindowVisible(hWnd))
                    return true;  // no es candidata, pero seguí con la siguiente

                // Filtro 2: descartar ventanas sin título (no son apps de usuario).
                int largo = GetWindowTextLength(hWnd);
                if (largo == 0)
                    return true;

                // Obtener el título.
                var sb = new StringBuilder(largo + 1);
                GetWindowText(hWnd, sb, sb.Capacity);
                string titulo = sb.ToString();

                // Obtener el nombre del proceso dueño de la ventana.
                string nombreProceso = ObtenerNombreProceso(hWnd);

                // FILTRO WHITELIST: ¿este programa está en nuestro catálogo?
                // Si no lo soportamos, no es candidata (y de paso esto excluye
                // solo el escritorio, el sistema, Chrome, la propia OneBlack, etc.).
                var programa = CatalogoDeProgramas.Buscar(nombreProceso);
                if (programa == null)
                    return true;  // no soportado: ignorar, seguir enumerando

                candidatas.Add(new VentanaCandidata
                {
                    Hwnd = hWnd,
                    Titulo = titulo,
                    NombreProceso = nombreProceso,
                    Programa = programa
                });

                return true;
              
            };

            EnumWindows(callback, IntPtr.Zero);
            return candidatas;
        }

        /// <summary>
        /// Dado el HWND de una ventana, averigua el nombre del proceso que la creó.
        /// Ej: para VS Code devuelve "Code"; para Notepad, "notepad".
        /// </summary>
        private string ObtenerNombreProceso(IntPtr hWnd)
        {
            try
            {
                GetWindowThreadProcessId(hWnd, out uint pid);
                using var proceso = Process.GetProcessById((int)pid);
                return proceso.ProcessName;  // nombre del .exe sin extensión
            }
            catch
            {
                return "";  // el proceso pudo morir entre medio; lo ignoramos
            }
        }
    }
}