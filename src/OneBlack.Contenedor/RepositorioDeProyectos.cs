using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace OneBlack.Contenedor
{
    /// <summary>
    /// Persiste los proyectos en un JSON local, en la carpeta de datos del usuario
    /// (%APPDATA%\OneBlack\proyectos.json). Sin login ni servidor: OneBlack es local
    /// single-user, el archivo por-usuario alcanza. Serializa con System.Text.Json
    /// (viene en .NET, sin dependencias).
    /// </summary>
    public class RepositorioDeProyectos
    {
        private readonly string rutaArchivo;
        private readonly JsonSerializerOptions opciones = new() { WriteIndented = true };

        public RepositorioDeProyectos()
        {
            // %APPDATA% = C:\Users\<vos>\AppData\Roaming. Guardamos bajo OneBlack\.
            string carpetaDatos = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "OneBlack");
            Directory.CreateDirectory(carpetaDatos);   // no-op si ya existe
            rutaArchivo = Path.Combine(carpetaDatos, "proyectos.json");
        }

        /// <summary>
        /// Carga todos los proyectos, ordenados por uso más reciente primero. Si el
        /// archivo no existe (primer arranque) o está corrupto, devuelve lista vacía.
        /// </summary>
        public List<Proyecto> CargarTodos()
        {
            if (!File.Exists(rutaArchivo))
                return new List<Proyecto>();

            try
            {
                string json = File.ReadAllText(rutaArchivo);
                var proyectos = JsonSerializer.Deserialize<List<Proyecto>>(json)
                                ?? new List<Proyecto>();
                return proyectos.OrderByDescending(p => p.UltimoUso).ToList();
            }
            catch
            {
                // Archivo dañado: no reventamos la app, arrancamos vacíos.
                return new List<Proyecto>();
            }
        }

        /// <summary>
        /// Alta o actualización: si ya existe un proyecto con el mismo Id lo reemplaza,
        /// si no lo agrega. Después reescribe el archivo entero.
        /// </summary>
        public void Guardar(Proyecto proyecto)
        {
            var proyectos = CargarTodos();
            int indice = proyectos.FindIndex(p => p.Id == proyecto.Id);
            if (indice >= 0) proyectos[indice] = proyecto;
            else proyectos.Add(proyecto);
            Persistir(proyectos);
        }

        /// <summary>Elimina un proyecto por Id.</summary>
        public void Eliminar(string id)
        {
            var proyectos = CargarTodos();
            proyectos.RemoveAll(p => p.Id == id);
            Persistir(proyectos);
        }

        /// <summary>
        /// Marca un proyecto como usado recién (UltimoUso = ahora). Se llama al abrirlo,
        /// para que suba al tope de recientes.
        /// </summary>
        public void MarcarUsado(string id)
        {
            var proyectos = CargarTodos();
            var proyecto = proyectos.FirstOrDefault(p => p.Id == id);
            if (proyecto == null) return;
            proyecto.UltimoUso = DateTime.Now;
            Persistir(proyectos);
        }

        // Escribe la lista completa (serialización total, no incremental: son pocos
        // proyectos, no vale la pena complicarlo).
        private void Persistir(List<Proyecto> proyectos) =>
            File.WriteAllText(rutaArchivo, JsonSerializer.Serialize(proyectos, opciones));
    }
}