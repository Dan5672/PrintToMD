using Print2Md.Core;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;

namespace Print2Md.Tasks;

internal sealed class StagedAssetSink : IAssetSink
{
    private readonly string relativeFolder;
    private readonly Dictionary<string, StagedAsset> assets = new Dictionary<string, StagedAsset>(StringComparer.OrdinalIgnoreCase);

    public StagedAssetSink(string relativeFolder)
    {
        this.relativeFolder = relativeFolder;
    }

    public async Task<AssetReference> WriteAsync(AssetContent asset, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var normalized = await NormalizeAsync(asset.Bytes, asset.ContentType, cancellationToken);
        var fileName = asset.ContentHash.Substring(0, Math.Min(24, asset.ContentHash.Length)) + normalized.Extension;
        if (!assets.ContainsKey(fileName))
        {
            assets.Add(fileName, new StagedAsset(normalized.Bytes, normalized.ContentType));
        }

        return new AssetReference(relativeFolder + "/" + fileName, normalized.ContentType);
    }

    public async Task CommitAsync(StorageFolder parentFolder, CancellationToken cancellationToken)
    {
        if (assets.Count == 0)
        {
            return;
        }

        var assetFolder = await parentFolder.CreateFolderAsync(relativeFolder, CreationCollisionOption.OpenIfExists);
        foreach (var item in assets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var file = await assetFolder.CreateFileAsync(item.Key, CreationCollisionOption.OpenIfExists);
            using (var output = await file.OpenStreamForWriteAsync())
            {
                output.SetLength(0);
                await output.WriteAsync(item.Value.Bytes, 0, item.Value.Bytes.Length, cancellationToken);
                await output.FlushAsync(cancellationToken);
            }
        }
    }

    private static async Task<NormalizedImage> NormalizeAsync(byte[] bytes, string contentType, CancellationToken cancellationToken)
    {
        switch (contentType.ToLowerInvariant())
        {
            case "image/png":
                return new NormalizedImage(bytes, ".png", "image/png");
            case "image/jpeg":
            case "image/jpg":
                return new NormalizedImage(bytes, ".jpg", "image/jpeg");
            case "image/gif":
                return new NormalizedImage(bytes, ".gif", "image/gif");
        }

        cancellationToken.ThrowIfCancellationRequested();
        using (var inputStream = new MemoryStream(bytes).AsRandomAccessStream())
        using (var outputStream = new InMemoryRandomAccessStream())
        {
            var decoder = await BitmapDecoder.CreateAsync(inputStream);
            var pixelData = await decoder.GetPixelDataAsync(
                BitmapPixelFormat.Bgra8,
                BitmapAlphaMode.Premultiplied,
                new BitmapTransform(),
                ExifOrientationMode.RespectExifOrientation,
                ColorManagementMode.ColorManageToSRgb);
            var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, outputStream);
            encoder.SetPixelData(
                BitmapPixelFormat.Bgra8,
                BitmapAlphaMode.Premultiplied,
                decoder.PixelWidth,
                decoder.PixelHeight,
                decoder.DpiX,
                decoder.DpiY,
                pixelData.DetachPixelData());
            await encoder.FlushAsync();
            outputStream.Seek(0);
            using (var managedOutput = outputStream.AsStreamForRead())
            using (var copy = new MemoryStream())
            {
                await managedOutput.CopyToAsync(copy, 81920, cancellationToken);
                return new NormalizedImage(copy.ToArray(), ".png", "image/png");
            }
        }
    }

    private sealed class StagedAsset
    {
        public StagedAsset(byte[] bytes, string contentType)
        {
            Bytes = bytes;
            ContentType = contentType;
        }

        public byte[] Bytes { get; }

        public string ContentType { get; }
    }

    private sealed class NormalizedImage
    {
        public NormalizedImage(byte[] bytes, string extension, string contentType)
        {
            Bytes = bytes;
            Extension = extension;
            ContentType = contentType;
        }

        public byte[] Bytes { get; }

        public string Extension { get; }

        public string ContentType { get; }
    }
}
