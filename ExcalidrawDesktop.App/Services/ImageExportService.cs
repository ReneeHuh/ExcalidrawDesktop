using ExcalidrawDesktop.Core;
using Microsoft.UI.Xaml;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;
using WinRT.Interop;

namespace ExcalidrawDesktop.App.Services;

internal sealed class ImageExportService
{
    private static readonly byte[] PngSignature =
        [137, 80, 78, 71, 13, 10, 26, 10];

    public async Task<StorageFile?> PickDestinationAsync(
        Window window,
        string displayName)
    {
        var picker = new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.PicturesLibrary,
            SuggestedFileName = ImageExportPolicy.CreateSuggestedBaseName(displayName),
        };
        picker.FileTypeChoices.Add("PNG image", new[] { ".png" });
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(window));
        return await picker.PickSaveFileAsync();
    }

    public static async Task WritePngAsync(
        StorageFile destination,
        IRandomAccessStream source,
        CancellationToken cancellationToken)
    {
        using var transaction = await destination.OpenTransactedWriteAsync();
        transaction.Stream.Size = 0;
        transaction.Stream.Seek(0);
        using var input = source.AsStreamForRead();
        using var output = transaction.Stream.AsStreamForWrite();
        var buffer = new byte[81_920];
        long total = 0;
        var signatureLength = 0;
        while (true)
        {
            var read = await input.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                break;
            }

            total += read;
            if (total > ImageExportPolicy.MaxPngBytes)
            {
                throw new BridgeProtocolException(
                    "ImageExportTooLarge",
                    "The PNG exceeds the 100 MB export limit.");
            }

            var signatureCopyLength = Math.Min(
                read,
                PngSignature.Length - signatureLength);
            if (signatureCopyLength > 0)
            {
                if (!buffer.AsSpan(0, signatureCopyLength).SequenceEqual(
                    PngSignature.AsSpan(signatureLength, signatureCopyLength)))
                {
                    throw new BridgeProtocolException(
                        "ImageExportInvalid",
                        "The editor returned invalid PNG data.");
                }
                signatureLength += signatureCopyLength;
            }

            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }

        if (total == 0 || signatureLength != PngSignature.Length)
        {
            throw new BridgeProtocolException(
                "ImageExportInvalid",
                "The editor returned an empty or invalid PNG image.");
        }

        await output.FlushAsync(cancellationToken);
        await transaction.CommitAsync();
    }
}
