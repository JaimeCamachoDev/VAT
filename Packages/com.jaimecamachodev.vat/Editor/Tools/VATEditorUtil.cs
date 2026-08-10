// File: VATEditorUtil.cs
// Author: JaimeCamachoDev
// Purpose: Utilidades editor-only centralizadas para el paquete VAT.
// License: MIT

using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace JaimeCamacho.VAT.Editor
{
    /// <summary>
    /// Conjunto de utilidades comunes para las herramientas VAT en el Editor.
    /// - Normaliza y valida rutas "project-relative" (Assets/...).
    /// - Asegura la existencia de carpetas de salida.
    /// - Sanea nombres de archivo.
    /// - Copia propiedades de materiales (Editor-only).
    /// - Helpers gráficos (RenderTexture -> Texture2D) y lectura de texturas.
    /// 
    /// Todas las funciones son estáticas y "Editor-only".
    /// </summary>
    internal static class VATEditorUtil
    {
        /// <summary>
        /// Convierte una ruta absoluta o relativa en una ruta relativa al proyecto (que empiece por "Assets").
        /// Si no es posible (p.ej. apunta fuera del proyecto), devuelve string.Empty.
        /// </summary>
        public static string ConvertToProjectRelativePath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return string.Empty;

            string normalizedPath = path.Replace('\\', '/').Trim();
            if (normalizedPath.Length == 0)
                return string.Empty;

            // Si ya es "Assets/..." la normalizamos y listo
            if (normalizedPath.StartsWith("Assets", StringComparison.OrdinalIgnoreCase))
                return NormalizeProjectRelativePath(normalizedPath);

            // Si es absoluta dentro del proyecto, la convertimos
            string dataPath = Application.dataPath.Replace('\\', '/');
            if (normalizedPath.StartsWith(dataPath, StringComparison.OrdinalIgnoreCase))
            {
                string relative = "Assets" + normalizedPath.Substring(dataPath.Length);
                return NormalizeProjectRelativePath(relative);
            }

            // Fuera del proyecto -> inválido
            return string.Empty;
        }

        /// <summary>
        /// Normaliza una ruta "project-relative". Garantiza:
        /// - Separadores "/"
        /// - Sin "./" ni "../"
        /// - Siempre comienza por "Assets"
        /// Devuelve string.Empty si no es una ruta válida dentro de Assets.
        /// </summary>
        public static string NormalizeProjectRelativePath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return string.Empty;

            string sanitized = path.Replace('\\', '/').Trim();
            if (sanitized.Length == 0)
                return string.Empty;

            string[] segments = sanitized.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length == 0)
                return string.Empty;

            if (!segments[0].Equals("Assets", StringComparison.OrdinalIgnoreCase))
                return string.Empty;

            // Rechazamos ./ y ../ para no permitir escapes
            for (int i = 1; i < segments.Length; i++)
            {
                if (segments[i] == "." || segments[i] == "..")
                    return string.Empty;
            }

            if (segments.Length == 1)
                return "Assets";

            return "Assets/" + string.Join("/", segments, 1, segments.Length - 1);
        }

        /// <summary>
        /// Devuelve true si la ruta project-relative apunta a una carpeta válida (o es un path creable dentro de Assets).
        /// - Permite rutas que aún no existen en disco, siempre que no apunten a un archivo.
        /// - Rechaza rutas fuera de "Assets".
        /// </summary>
        public static bool IsProjectRelativeFolder(string projectRelativePath)
        {
            string normalized = NormalizeProjectRelativePath(projectRelativePath);
            if (string.IsNullOrEmpty(normalized))
                return false;

            // Si ya es una carpeta del AssetDatabase -> true
            if (AssetDatabase.IsValidFolder(normalized))
                return true;

            // Si es un archivo existente -> false (no es carpeta)
            if (!TryGetProjectRoot(out string projectRoot))
                return false;

            string absolute = Path.Combine(projectRoot, normalized);
            if (File.Exists(absolute))
                return false;

            // Si no existe, pero no parece archivo, lo consideramos "carpeta válida creable"
            return true;
        }

        /// <summary>
        /// Asegura que exista la carpeta indicada por ruta project-relative (Assets/…).
        /// Crea la jerarquía en disco si fuese necesario y hace Refresh.
        /// </summary>
        public static bool EnsureDirectoryExists(string projectRelativePath, bool logErrors = true)
        {
            string normalized = NormalizeProjectRelativePath(projectRelativePath);
            if (string.IsNullOrEmpty(normalized))
            {
                if (logErrors) Debug.LogError("VAT: La ruta de salida no puede estar vacía o fuera de 'Assets'.");
                return false;
            }

            if (!TryGetProjectRoot(out string projectRoot))
            {
                if (logErrors) Debug.LogError("VAT: No se pudo determinar la ruta raíz del proyecto.");
                return false;
            }

            string absolute = Path.Combine(projectRoot, normalized).Replace('\\', '/');
            if (Directory.Exists(absolute))
                return true;

            try
            {
                Directory.CreateDirectory(absolute);
                AssetDatabase.Refresh();
                return true;
            }
            catch (Exception e)
            {
                if (logErrors) Debug.LogError($"VAT: No se pudo crear el directorio '{absolute}'. {e.Message}");
                return false;
            }
        }

        /// <summary>
        /// Combina carpeta y archivo (ambos project-relative o sólo nombre de archivo) usando separadores '/'.
        /// No valida la existencia en disco; es pura manipulación de string.
        /// </summary>
        public static string CombineProjectRelativePath(string folder, string fileName)
        {
            if (string.IsNullOrEmpty(folder))
                return fileName ?? string.Empty;

            folder = folder.Replace('\\', '/').TrimEnd('/');

            if (string.IsNullOrEmpty(fileName))
                return folder;

            return $"{folder}/{fileName}";
        }

        /// <summary>
        /// Devuelve un nombre de archivo "seguro" (sin caracteres inválidos del SO).
        /// Mantiene la extensión si está presente y es válida.
        /// </summary>
        public static string SanitizeFileName(string candidate, string fallbackBaseName = "NewFile")
        {
            string working = string.IsNullOrWhiteSpace(candidate) ? fallbackBaseName : candidate.Trim();
            if (string.IsNullOrEmpty(working))
                working = fallbackBaseName;

            char[] invalid = Path.GetInvalidFileNameChars();
            var sb = new StringBuilder(working.Length);
            foreach (char c in working)
                sb.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);

            string sanitized = sb.ToString();
            if (string.IsNullOrEmpty(sanitized))
                sanitized = fallbackBaseName;

            return sanitized;
        }

        /// <summary>
        /// Sanea un nombre de atlas garantizando extensión .png y devolviendo el "baseName" (sin extensión) por out.
        /// Firma compatible con la que usa tu VATsToolWindow.
        /// </summary>
        public static string SanitizeAtlasFileName(string candidate, string defaultName, out string sanitizedBaseName)
        {
            sanitizedBaseName = string.Empty;

            string workingName = string.IsNullOrWhiteSpace(candidate) ? defaultName : candidate.Trim();
            if (string.IsNullOrEmpty(workingName))
                workingName = defaultName;

            // Quitar caracteres inválidos
            char[] invalid = Path.GetInvalidFileNameChars();
            var sb = new StringBuilder(workingName.Length);
            foreach (char c in workingName)
                sb.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);

            string sanitized = sb.ToString();
            if (string.IsNullOrEmpty(sanitized))
                sanitized = defaultName;

            // Asegurar .png
            string ext = Path.GetExtension(sanitized);
            if (string.Equals(ext, ".png", StringComparison.OrdinalIgnoreCase))
            {
                sanitizedBaseName = Path.GetFileNameWithoutExtension(sanitized);
                return sanitized;
            }

            if (!string.IsNullOrEmpty(ext))
                sanitized = sanitized.Substring(0, sanitized.Length - ext.Length);

            if (string.IsNullOrEmpty(sanitized))
                sanitized = defaultName;

            sanitizedBaseName = sanitized;
            return sanitized + ".png";
        }

        /// <summary>
        /// Intenta obtener la raíz del proyecto (carpeta que contiene "Assets").
        /// </summary>
        public static bool TryGetProjectRoot(out string projectRoot)
        {
            string dataPath = Application.dataPath; // .../MyProject/Assets
            projectRoot = Path.GetDirectoryName(dataPath);
            return !string.IsNullOrEmpty(projectRoot);
        }

        /// <summary>
        /// Copia todas las propiedades públicas del shader de un material origen a uno destino.
        /// Útil cuando generas materiales VAT nuevos a partir de uno de referencia.
        /// </summary>
        public static void CopyMaterialProperties(Material source, Material destination)
        {
            if (source == null || destination == null)
                return;

            Shader srcShader = source.shader;
            if (srcShader == null)
                return;

            int count = ShaderUtil.GetPropertyCount(srcShader);
            for (int i = 0; i < count; i++)
            {
                string propName = ShaderUtil.GetPropertyName(srcShader, i);
                ShaderUtil.ShaderPropertyType type = ShaderUtil.GetPropertyType(srcShader, i);

                switch (type)
                {
                    case ShaderUtil.ShaderPropertyType.Color:
                        destination.SetColor(propName, source.GetColor(propName));
                        break;

                    case ShaderUtil.ShaderPropertyType.Vector:
                        destination.SetVector(propName, source.GetVector(propName));
                        break;

                    case ShaderUtil.ShaderPropertyType.Float:
                    case ShaderUtil.ShaderPropertyType.Range:
                        destination.SetFloat(propName, source.GetFloat(propName));
                        break;

                    case ShaderUtil.ShaderPropertyType.TexEnv:
                        destination.SetTexture(propName, source.GetTexture(propName));
                        // Opcional: copiar escala/offset de la textura si aplicase
                        // destination.SetTextureScale(propName, source.GetTextureScale(propName));
                        // destination.SetTextureOffset(propName, source.GetTextureOffset(propName));
                        break;
                }
            }
        }

        /// <summary>
        /// Comprueba si una Texture2D es legible (isReadable o import settings).
        /// Devuelve false y explica el motivo en "reason" si no lo es.
        /// </summary>
        public static bool IsTextureReadable(Texture2D texture, out string reason)
        {
            reason = string.Empty;

            if (texture == null)
            {
                reason = "La textura proporcionada es nula.";
                return false;
            }

            if (texture.isReadable)
                return true;

            string assetPath = AssetDatabase.GetAssetPath(texture);
            if (!string.IsNullOrEmpty(assetPath))
            {
                if (AssetImporter.GetAtPath(assetPath) is TextureImporter importer)
                {
                    if (!importer.isReadable)
                    {
                        reason = $"La textura '{texture.name}' no es legible. Habilita Read/Write en su importador.";
                        return false;
                    }
                }
            }

            reason = $"La textura '{texture.name}' no permite lectura en modo de edición.";
            return false;
        }

        /// <summary>
        /// Convierte un RenderTexture en un Texture2D (RGBAHalf por defecto).
        /// No registra el asset; sólo crea el objeto en memoria.
        /// </summary>
        public static Texture2D RenderTextureToTexture2D(RenderTexture rt, TextureFormat format = TextureFormat.RGBAHalf, bool mipChain = false)
        {
            if (rt == null)
                throw new ArgumentNullException(nameof(rt));

            Texture2D tex = new Texture2D(rt.width, rt.height, format, mipChain);

            RenderTexture prev = RenderTexture.active;
            RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;

            return tex;
        }
    }
}
