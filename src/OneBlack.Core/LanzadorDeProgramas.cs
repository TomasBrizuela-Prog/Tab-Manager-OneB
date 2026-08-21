using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace OneBlack.Core
{
    /// <summary>
    /// Lanza un programa (opcionalmente apuntando a una carpeta) y espera a que
    /// su ventana principal aparezca, para poder adoptarla. Es el ladrillo del
    /// sistema de proyectos: "OneBlack lanza los programas" con los flags correctos.
    ///
    /// El problema que resuelve: entre ejecutar `code C:\ruta` y que exista una
    /// ventana adoptable pasan segundos. El HWND no existe hasta entonces, así que
    /// hay que hacer polling hasta encontrarlo (o rendirse tras un timeout).
    /// </summary>
    public class LanzadorDeProgramas
    {
        private readonly BuscadorDeVentanas buscador = new BuscadorDeVentanas();

        /// <summary>
        /// Lanza el programa y espera (async) a que aparezca su ventana adoptable.
        /// Devuelve el HWND de la ventana nueva, o IntPtr.Zero si no apareció
        /// dentro del timeout.
        /// </summary>
        public async Task<IntPtr> LanzarYEsperar(
            ProgramaSoportado programa, string? carpeta, int timeoutMs = 15000)
        {
            // 1. Anotar qué ventanas de ESTE programa ya existían antes de lanzar.
            //    Cuando aparezca una que no estaba, sabemos que es la que lanzamos
            //    nosotros y no una que el usuario ya tenía abierta.
            var previas = new HashSet<IntPtr>(
                buscador.BuscarCandidatas()
                        .Where(c => c.Programa.NombreProceso == programa.NombreProceso)
                        .Select(c => c.Hwnd));

            // 2. Lanzar el proceso.
            if (!Lanzar(programa, carpeta))
                return IntPtr.Zero;

            // 3. Polling: cada 300ms, buscar una ventana NUEVA de este programa.
            var reloj = Stopwatch.StartNew();
            while (reloj.ElapsedMilliseconds < timeoutMs)
            {
                await Task.Delay(300);

                var candidata = buscador.BuscarCandidatas()
                    .FirstOrDefault(c =>
                        c.Programa.NombreProceso == programa.NombreProceso
                        && !previas.Contains(c.Hwnd));

                if (candidata != null)
                    return candidata.Hwnd;   // apareció: la devolvemos para adoptar
            }

            return IntPtr.Zero;   // timeout: la ventana nunca apareció
        }

        /// <summary>
        /// Lanza el proceso. Arma los argumentos con los flags base del programa
        /// y, si corresponde, la carpeta a abrir.
        /// </summary>
        private bool Lanzar(ProgramaSoportado programa, string? carpeta)
        {
            try
            {
                // Empezamos con los flags fijos (ej: "--disable-gpu").
                string args = programa.ArgumentosBase;

                // Si el programa usa carpeta y nos dieron una, la agregamos entre comillas.
                if (programa.UsaCarpeta && !string.IsNullOrWhiteSpace(carpeta))
                    args = $"{args} \"{carpeta}\"".Trim();

                var inicio = new ProcessStartInfo
                {
                    FileName = programa.ComandoRelanzar,
                    Arguments = args,
                    UseShellExecute = true   // usa el PATH del sistema (encuentra `code`)
                };

                Process.Start(inicio);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}