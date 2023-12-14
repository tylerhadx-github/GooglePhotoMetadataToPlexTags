using System.Data.SQLite;
using Newtonsoft.Json;

Console.WriteLine("First you need to get a plex token from web app");
Console.WriteLine("Starting Plex Tag Update");
Console.WriteLine("Opening Folder: " + SharedData.rootFolder);

ProcessFolder();

Console.WriteLine("Press Enter to exit..."); //allows process to stay on while async process comes back
Console.ReadLine();

static async void ProcessFolder()
{
    foreach (string filePath in Directory.EnumerateFiles(SharedData.rootFolder, "*.json", SearchOption.AllDirectories))
    {
        string jsonContent = File.ReadAllText(filePath);
        if (jsonContent != null)
        {
            try
            {
                ImageMetadata metadata = JsonConvert.DeserializeObject<ImageMetadata>(jsonContent);

                if (metadata?.People != null)
                {
                    foreach (Person person in metadata.People)
                    {

                        long? metadataId = LookupMetadataItem(metadata.title);
                        long? tagId = null;
                        if (metadataId != null)
                        {
                            tagId = await LookupTagsAsync(person.Name, metadataId);

                            if (tagId != null)
                            {
                                if (!CheckIfTagExisitOnMetadata(metadataId, tagId))
                                {
                                    Console.WriteLine($"File: {Path.GetFileName(filePath)}, Person: {person?.Name ?? "Unknown"}");
                                    InsertIntoTaggingsTable(metadataId, tagId);
                                }
                            }
                        }
                        else
                        {
                            Console.WriteLine($"File: {Path.GetFileName(filePath)} Not Found In SQLite");
                        }

                        tagId = null;
                        metadataId = null;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(jsonContent);
            }
        }
    }
}

static bool CheckIfTagExisitOnMetadata(long? metadataId, long? tagId)
{
    using (SQLiteConnection connection = new SQLiteConnection($"Data Source={SharedData.databasePath};Version=3;"))
    {
        connection.Open();


        string query = $"SELECT COUNT(*) FROM tags WHERE id = @TagId AND metadata_item_id = @MetadataId";
        using (SQLiteCommand command = new SQLiteCommand(query, connection))
        {
            command.Parameters.AddWithValue("@TagId", tagId);
            command.Parameters.AddWithValue("@MetadataId", metadataId);

            int result = Convert.ToInt32(command.ExecuteScalar());

            return result > 0;
        }
    }
}

static async Task<long?> LookupTagsAsync(string searchTag, long? metadataId)
{
    using (SQLiteConnection connection = new SQLiteConnection($"Data Source={SharedData.databasePath};Version=3;"))
    {
        connection.Open();

        searchTag = OverrideTag(searchTag.Trim());

        string query = $"SELECT id FROM tags WHERE tag = @PersonName and tag_type = 0;";
        using (SQLiteCommand command = new SQLiteCommand(query, connection))
        {
            command.Parameters.AddWithValue("@PersonName", searchTag.Trim());
            object result = command.ExecuteScalar();

            if (result != null && result != DBNull.Value)
            {
                return (long)result;
            }
            else
            {
                await CreatePlexTagThroughAPI(searchTag, metadataId);
                return await LookupTagsAsync(searchTag, metadataId); //this will now returned the created one.
            }
        }
    }
}

static async Task CreatePlexTagThroughAPI(string tag, long? media_id)
{
    try
    {
        string encodedTag = Uri.EscapeDataString(tag);
        var url = SharedData.localPlexUrl + "library/sections/" + SharedData.libraryId + "/all?type=13&id=" + media_id + "&includeExternalMedia=1&tag%5B0%5D.tag.tag=" + encodedTag + "&X-Plex-Product=Plex%20Web&X-Plex-Version=4.100.1&X-Plex-Client-Identifier=sxhdm3a6rnahyfl451fom9br&X-Plex-Platform=Microsoft%20Edge&X-Plex-Platform-Version=120.0&X-Plex-Features=external-media%2Cindirect-media%2Chub-style-list&X-Plex-Model=bundled&X-Plex-Device=Windows&X-Plex-Device-Name=Microsoft%20Edge&X-Plex-Device-Screen-Resolution=1872x924%2C1920x1080&" + SharedData.plexToken + "&X-Plex-Language=en";

        using (HttpClient client = new HttpClient())
        {
            client.DefaultRequestHeaders.Add("Accept", "application/xml");
            client.DefaultRequestHeaders.Add("Accept-Language", "en");
            client.DefaultRequestHeaders.Add("Connection", "keep-alive");
            client.DefaultRequestHeaders.Add("DNT", "1");
            client.DefaultRequestHeaders.Add("Origin", "http://127.0.0.1:32400");
            client.DefaultRequestHeaders.Add("Referer", "http://127.0.0.1:32400/web/index.html");
            client.DefaultRequestHeaders.Add("Sec-Fetch-Dest", "empty");
            client.DefaultRequestHeaders.Add("Sec-Fetch-Mode", "cors");
            client.DefaultRequestHeaders.Add("Sec-Fetch-Site", "same-origin");
            client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36 Edg/120.0.0.0");
            client.DefaultRequestHeaders.Add("X-Requested-With", "XMLHttpRequest");
            client.DefaultRequestHeaders.Add("sec-ch-ua", "\"Not_A Brand\";v=\"8\", \"Chromium\";v=\"120\", \"Microsoft Edge\";v=\"120\"");
            client.DefaultRequestHeaders.Add("sec-ch-ua-mobile", "?0");
            client.DefaultRequestHeaders.Add("sec-ch-ua-platform", "\"Windows\"");
            var tempurl = new Uri(url);

            HttpResponseMessage response = await client.PutAsync(tempurl, new StringContent(""));

            if (response.IsSuccessStatusCode)
            {
                Console.WriteLine("PUT request successful");
            }
            else
            {
                Console.WriteLine($"PUT request failed with status code {response.StatusCode}");
            }
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error making PUT request: {ex.Message}");
    }

}

static string OverrideTag(string searchTag)
{
    if (searchTag == "Badly Spelled Person")
    {
        return "Fixed Name Of Person";
    }
    else
    {
        return searchTag;
    }
}

static long? LookupMetadataItem(string fileName)
{
    using (SQLiteConnection connection = new SQLiteConnection($"Data Source={SharedData.databasePath};Version=3;"))
    {
        connection.Open();
        string query = $"SELECT id FROM metadata_items WHERE title = @title;";
        using (SQLiteCommand command = new SQLiteCommand(query, connection))
        {
            command.Parameters.AddWithValue("@title", RemoveFileExtension(fileName));
            object result = command.ExecuteScalar();

            if (result != null && result != DBNull.Value)
            {
                return (long)result;
            }
            else
            {
                return null; //filenot found
            }
        }
    }
}

static void InsertIntoTaggingsTable(long? metadataItemId, long? tagId)
{
    using (SQLiteConnection connection = new SQLiteConnection($"Data Source={SharedData.databasePath};Version=3;"))
    {
        connection.Open();
        string tableName = "taggings";
        string insertQuery = $"INSERT INTO {tableName} (metadata_item_id, tag_id) " +
                            $"VALUES (@MetadataItemId, @TagId);";

        using (SQLiteCommand command = new SQLiteCommand(insertQuery, connection))
        {
            command.Parameters.AddWithValue("@MetadataItemId", metadataItemId);
            command.Parameters.AddWithValue("@TagId", tagId);
            command.ExecuteNonQuery();
        }
    }
}

static string RemoveFileExtension(string fileName)
{
    return Path.GetFileNameWithoutExtension(fileName);
}

class ImageMetadata
{
    public string title { get; set; }
    public List<Person> People { get; set; }
}

class Person
{
    public string Name { get; set; }
}

class SharedData
{
    public static List<string> MissingPersons { get; } = new List<string>();
    public static string rootFolder = @"D:\Pictures\";//Change
    public static string databasePath { get; } = @"C:/Users/{CurrentUser}/AppData/Local/Plex Media Server/Plug-in Support/Databases/com.plexapp.plugins.library.db";//Change
    public static string plexToken = "X-Plex-Token={PlexToken}";//Change
    public static string localPlexUrl = "http://127.0.0.1:32400/";//Change If Needed
    public static int libraryId = 9;//My library is 9 for photos update yours to match
}