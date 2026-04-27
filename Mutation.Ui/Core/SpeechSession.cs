using System;
using System.IO;

namespace Mutation.Ui;

public sealed record SpeechSession(string FilePath, DateTime Timestamp)
{
        public string FileName => Path.GetFileName(FilePath);

        public string Extension => Path.GetExtension(FilePath);
}
