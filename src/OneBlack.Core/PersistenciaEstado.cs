using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace OneBlack.Core
{
    /// <summary>
    /// Una entrada del archivo de estado: todo lo que el janitor necesita
    /// para devolver UNA ventana a su estado original, sin depender de
    /// la memoria de OneBlack (que ya no existe cuando el janitor actúa).
    /// </summary>
    public class VentanaPersistida
    {
        public long Hwnd { get; set; }            // la ventana adoptada
        public long PadreOriginal { get; set; }   // a quién devolverla
        public int EstilosOriginales { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
        public int Ancho { get; set; }
        public int Alto { get; set; }
    }

    /// <summary>
    /// Escribe y borra el archivo de estado de forma ATÓMICA (temporal + renombre),
    /// para que el janitor nunca lea un archivo a medias si OneBlack crashea
    /// justo mientras se escribe.
    /// </summary>
    public class PersistenciaEstado
    {
        // Ruta del archivo de estado. Lo ponemos en una carpeta de datos de la app
        // (no en la carpeta del proyecto), donde OneBlack y el janitor lo comparten.
        private static readonly string CarpetaDatos =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                         "OneBlack");

        private static readonly string RutaArchivo =
            Path.Combine(CarpetaDatos, "ventanas-adoptadas.json");

        private static readonly string RutaTemporal =
            Path.Combine(CarpetaDatos, "ventanas-adoptadas.tmp");

        /// <summary>Ruta pública, para que el janitor sepa qué archivo leer.</summary>
        public static string ObtenerRutaArchivo() => RutaArchivo;

        /// <summary>
        /// Guarda la lista completa de ventanas adoptadas, reescribiendo todo
        /// el archivo (enfoque A) de forma atómica.
        /// </summary>
        public void Guardar(List<VentanaPersistida> ventanas)
        {
            Directory.CreateDirectory(CarpetaDatos);  // crea la carpeta si no existe

            // 1. Serializar la lista completa a JSON.
            string json = JsonSerializer.Serialize(ventanas,
                new JsonSerializerOptions { WriteIndented = true });

            // 2. Escribir al archivo TEMPORAL (no al definitivo todavía).
            File.WriteAllText(RutaTemporal, json);

            // 3. Renombrar el temporal al definitivo: operación atómica.
            //    Si ya existe el definitivo, File.Move con overwrite lo reemplaza de golpe.
            File.Move(RutaTemporal, RutaArchivo, overwrite: true);
        }

        /// <summary>
        /// Borra el archivo de estado. Se llama en el cierre LIMPIO, cuando
        /// OneBlack ya devolvió sus ventanas y no hay nada que recuperar.
        /// </summary>
        public void Borrar()
        {
            if (File.Exists(RutaArchivo))
                File.Delete(RutaArchivo);
        }

        /// <summary>
        /// Lee el archivo de estado. Lo usa el JANITOR para saber qué restaurar.
        /// Devuelve lista vacía si no hay archivo (= cierre limpio, nada que hacer).
        /// </summary>
        public List<VentanaPersistida> Leer()
        {
            if (!File.Exists(RutaArchivo))
                return new List<VentanaPersistida>();

            string json = File.ReadAllText(RutaArchivo);
            return JsonSerializer.Deserialize<List<VentanaPersistida>>(json)
                   ?? new List<VentanaPersistida>();
        }
    }
}