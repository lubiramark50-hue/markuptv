using Microsoft.Maui.Storage;
using System;
using System.Threading.Tasks;

namespace MarkUptv.Services;

public interface IFileUploadService
{
    Task<string> UploadFileAsync(FileResult file, Action<double>? progress = null);
}