using System;
using System.Collections.Generic;
using System.IO;
using LanguageCore;
using LanguageCore.Workspaces;

namespace LanguageServer;

static class ConfigurationManager
{
    public static IReadOnlyList<(Uri Uri, string Content)> Search(Uri currentDocument)
    {
        Uri currentUri = currentDocument;
        List<(Uri Uri, string Content)> result = [];
        EndlessCheck endlessCheck = new(50);
        while (currentUri.LocalPath != "/")
        {
            if (endlessCheck.Step()) break;
            Uri uri = new(currentUri, $"./{Configuration.FileName}");
            if (File.Exists(uri.LocalPath))
            {
                result.Add((uri, File.ReadAllText(uri.LocalPath)));
            }
            currentUri = new Uri(currentUri, "..");
        }
        return result;
    }
}
