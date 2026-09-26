using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Security.Cryptography;
using System.Web.Script.Serialization;

namespace GlobalTranslator
{
    internal sealed class SavedConversation
    {
        public string Id = Guid.NewGuid().ToString("N");
        public string Title = "新沟通";
        public string[] Texts = new string[0];
        public string Language = "auto";
        [ScriptIgnore]
        public byte[][] Images = new byte[0][];
        public string[] EncodedImages
        {
            get { return Images.Select(Convert.ToBase64String).ToArray(); }
            set { Images = (value ?? new string[0]).Select(Convert.FromBase64String).ToArray(); }
        }
        public CommunicationTurn[] Turns = new CommunicationTurn[0];
        public CommerceDocument[] Documents = new CommerceDocument[0];
        public InquiryField[] InquiryFields = new InquiryField[0];
        public string[] MissingFields = new string[0];
        public bool Edited;
        public bool Stale;
        public override string ToString() { return Title; }
    }

    internal static class ConversationStore
    {
        internal static string DirectoryPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GlobalTranslator", "Conversations");
        private static string FilePath(string id)
        {
            Guid parsed;
            if (!Guid.TryParseExact(id, "N", out parsed)) throw new InvalidDataException("无效会话标识。");
            return Path.Combine(DirectoryPath, id + ".bin");
        }
        internal static void Save(SavedConversation value)
        {
            Directory.CreateDirectory(DirectoryPath);
            var serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            byte[] encrypted = ProtectedData.Protect(Encoding.UTF8.GetBytes(serializer.Serialize(value)),
                null, DataProtectionScope.CurrentUser);
            string path = FilePath(value.Id), temporary = path + ".tmp";
            try
            {
                File.WriteAllBytes(temporary, encrypted);
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        internal static SavedConversation Load(string path)
        {
            byte[] clear = ProtectedData.Unprotect(File.ReadAllBytes(path), null, DataProtectionScope.CurrentUser);
            var result = new JavaScriptSerializer { MaxJsonLength = int.MaxValue }
                .Deserialize<SavedConversation>(Encoding.UTF8.GetString(clear));
            FilePath(result.Id);
            if (result.Texts == null || result.Turns == null || result.Images.Length > 5 ||
                result.Images.Any(image => image.Length == 0 || image.Length > 10 * 1024 * 1024))
                throw new InvalidDataException("会话内容无效，原文件已保留。");
            if (result.Documents == null) result.Documents = new CommerceDocument[0];
            return result;
        }
        internal static string[] Files()
        {
            return Directory.Exists(DirectoryPath) ? Directory.GetFiles(DirectoryPath, "*.bin")
                .OrderByDescending(File.GetLastWriteTimeUtc).ToArray() : new string[0];
        }
        internal static void Delete(string id) { File.Delete(FilePath(id)); }
    }
}
