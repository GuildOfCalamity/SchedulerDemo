using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Con = System.Diagnostics.Debug;

namespace SchedulerDemo
{
    public class Serializer
    {
        /// <summary>
        /// Read and deserialize file data into generic type.
        /// </summary>
        /// <example>
        /// Profile _profile = Serializer.Load<Profile>(System.IO.Path.Combine(Environment.CurrentDirectory, "profile.json"));
        /// </example>
        public static T Load<T>(string path) where T : new()
        {
            try
            {
                if (File.Exists(path))
                    return JsonSerializer.Deserialize<T>(File.ReadAllText(path));
                else
                    return new T();
            }
            catch (Exception ex)
            {
                Con.WriteLine($"[Serializer.Load]: {ex.Message}");
                return new T();
            }
        }

        /// <summary>
        /// Test method for returning multiple profile based on a matched setting.
        /// </summary>
        /// <param name="path"></param>
        /// <returns></returns>
        public static List<Settings> GetActives(string path)
        {
            try
            {
                var profs = JsonSerializer.Deserialize<List<Settings>>(File.ReadAllText(path)).Where(o => o.LastUse?.Month == DateTime.Now.Month).ToList();
                return profs;
            }
            catch (Exception ex)
            {
                Con.WriteLine($"[Serializer.GetActives]: {ex.Message}");
                return new List<Settings>();
            }
        }

        /// <summary>
        /// Serialize a <see cref="Profile"/> object and write to file.
        /// </summary>
        /// <example>
        /// var _profile = new Profile { option1 = "thing1", option2 = "thing2" };
        /// _profile.Save(Path.Combine(Environment.CurrentDirectory, "profile.json"));
        /// </example>
        public bool Save(string path)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, JsonSerializer.Serialize(this, typeof(Settings), new JsonSerializerOptions { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, WriteIndented = true }));
                return true;
            }
            catch (Exception ex)
            {
                Con.WriteLine($"[Serializer.Save]: {ex.Message}");
                return false;
            }
        }
    }

    public class Settings : Serializer
    {
        public string? User { get; set; }
        public string? Location { get; set; }
        public DateTime? LastUse { get; set; }
    }

}
