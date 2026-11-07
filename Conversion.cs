#region Related components
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SkiaSharp;
#endregion

namespace net.vieapps.Components.Utility
{
	public static partial class ImageExtensions
	{
		/// <summary>
		/// Gets or Sets the state for resizing big WebP image
		/// </summary>
		public static bool ResizeBigWebpImage { get; set; } = "true".IsEquals(UtilityService.GetAppSetting("Images:WebP:ResizeBigImage", "true"));

		/// <summary>
		/// Gets or Sets max-width of WebP image
		/// </summary>
		public static int WebpMaxWidth { get; set; } = Int32.TryParse(UtilityService.GetAppSetting("Images:WebP:MaxWidth"), out var maxWidth) && maxWidth > 0 ? maxWidth : 1920;

		static int GetQualitySetting(string name, int defaultValue)
			=> Int32.TryParse(UtilityService.GetAppSetting(name, defaultValue.ToString()), out var quality) ? Math.Clamp(quality, 1, 100) : defaultValue;

		static int WebpQuality { get; } = ImageExtensions.GetQualitySetting("Images:WebP:Quality", 70);

		static int WebpDefaultQuality { get; } = ImageExtensions.GetQualitySetting("Images:WebP:DefaultQuality", 75);

		static int JpegQuality { get; } = ImageExtensions.GetQualitySetting("Images:Jpeg:Quality", 75);

		static bool IsSameFormat(this string format, string other)
			=> format != null && other != null && format.IsEquals(other);

		static SKBitmap DecodeBitmap(this Stream stream)
		{
			stream.Seek(0, SeekOrigin.Begin);
			using var data = SKData.Create(stream);
			return data.DecodeBitmap();
		}

		static SKBitmap DecodeBitmap(this byte[] bytes)
		{
			using var data = SKData.CreateCopy(bytes);
			return data.DecodeBitmap();
		}

		static SKBitmap DecodeBitmap(this SKData data)
		{
			using var codec = SKCodec.Create(data) ?? throw new InvalidDataException("The data is not a supported image or the image is corrupted");
			var info = codec.Info;
			using var colorSpace = SKColorSpace.CreateSrgb();
			var bitmap = SKBitmap.Decode(codec, new SKImageInfo(info.Width, info.Height, SKColorType.Bgra8888, info.AlphaType == SKAlphaType.Opaque ? SKAlphaType.Opaque : SKAlphaType.Premul, colorSpace)) ?? throw new InvalidDataException("Cannot decode the image");
			var origin = codec.EncodedOrigin;

			if (origin == SKEncodedOrigin.TopLeft)
				return bitmap;

			try
			{
				return bitmap.ApplyOrigin(origin);
			}
			finally
			{
				bitmap.Dispose();
			}
		}

		static SKBitmap ApplyOrigin(this SKBitmap source, SKEncodedOrigin origin)
		{
			var width = source.Width;
			var height = source.Height;
			var swap = origin == SKEncodedOrigin.LeftTop || origin == SKEncodedOrigin.RightTop || origin == SKEncodedOrigin.RightBottom || origin == SKEncodedOrigin.LeftBottom;
			var target = new SKBitmap(source.Info.WithSize(swap ? height : width, swap ? width : height));

			using var canvas = new SKCanvas(target);
			switch (origin)
			{
				case SKEncodedOrigin.TopRight:
					canvas.Translate(width, 0);
					canvas.Scale(-1, 1);
					break;

				case SKEncodedOrigin.BottomRight:
					canvas.Translate(width, height);
					canvas.RotateDegrees(180);
					break;

				case SKEncodedOrigin.BottomLeft:
					canvas.Translate(0, height);
					canvas.Scale(1, -1);
					break;

				case SKEncodedOrigin.LeftTop:
					canvas.Scale(-1, 1);
					canvas.RotateDegrees(90);
					break;

				case SKEncodedOrigin.RightTop:
					canvas.Translate(height, 0);
					canvas.RotateDegrees(90);
					break;

				case SKEncodedOrigin.RightBottom:
					canvas.Translate(height, width);
					canvas.RotateDegrees(180);
					canvas.Scale(-1, 1);
					canvas.RotateDegrees(90);
					break;

				case SKEncodedOrigin.LeftBottom:
					canvas.Translate(0, width);
					canvas.RotateDegrees(-90);
					break;
			}

			canvas.DrawBitmap(source, 0, 0, new SKSamplingOptions(SKFilterMode.Linear));
			return target;
		}

		static SKSizeI GetFitSize(int sourceWidth, int sourceHeight, int boxWidth, int boxHeight)
		{
			var scale = boxWidth < 1 ? (double)boxHeight / sourceHeight : boxHeight < 1 ? (double)boxWidth / sourceWidth : Math.Min((double)boxWidth / sourceWidth, (double)boxHeight / sourceHeight);
			return new SKSizeI(Math.Max(1, (int)Math.Round(sourceWidth * scale)), Math.Max(1, (int)Math.Round(sourceHeight * scale)));
		}

		static SKBitmap ResizeTo(this SKBitmap source, SKSizeI size, bool highQuality)
		{
			var sampling = (double)size.Width / source.Width < 0.5 ? new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear) : new SKSamplingOptions(highQuality ? SKCubicResampler.CatmullRom : SKCubicResampler.Mitchell);
			return source.Resize(source.Info.WithSize(size.Width, size.Height), sampling) ?? throw new InvalidOperationException("Cannot resize the image");
		}

		static SKBitmap FlattenOnWhite(this SKBitmap source)
		{
			var flattened = new SKBitmap(new SKImageInfo(source.Width, source.Height, SKColorType.Bgra8888, SKAlphaType.Opaque));
			using var canvas = new SKCanvas(flattened);
			canvas.Clear(SKColors.White);
			canvas.DrawBitmap(source, 0, 0, new SKSamplingOptions(SKFilterMode.Linear));
			return flattened;
		}

		static MemoryStream EncodeBmp(this SKBitmap source)
		{
			using var converted = source.ColorType == SKColorType.Bgra8888 ? null : source.Copy(SKColorType.Bgra8888);
			var bitmap = converted ?? source;
			var hasAlpha = bitmap.AlphaType != SKAlphaType.Opaque;
			var width = bitmap.Width;
			var height = bitmap.Height;
			var rowBytes = bitmap.RowBytes;
			var rowSize = checked((width * 3 + 3) & ~3);
			var dataSize = checked(rowSize * height);
			var stream = UtilityService.CreateMemoryStream("bmp", checked(54 + dataSize));
			using var writer = new BinaryWriter(stream, System.Text.Encoding.ASCII, true);
			writer.Write((ushort)0x4D42);
			writer.Write((uint)(54 + dataSize));
			writer.Write(0u);
			writer.Write(54u);

			writer.Write(40u);
			writer.Write(width);
			writer.Write(height);
			writer.Write((ushort)1);
			writer.Write((ushort)24);
			writer.Write(0u);
			writer.Write((uint)dataSize);
			writer.Write(2835);
			writer.Write(2835);
			writer.Write(0u);
			writer.Write(0u);

			var pixels = bitmap.GetPixelSpan();
			var row = new byte[rowSize];

			for (var y = height - 1; y >= 0; y--)
			{
				var line = pixels.Slice(y * rowBytes, width * 4);
				for (var x = 0; x < width; x++)
				{
					var padding = hasAlpha ? 255 - line[x * 4 + 3] : 0;
					row[x * 3] = (byte)Math.Min(255, line[x * 4] + padding);
					row[x * 3 + 1] = (byte)Math.Min(255, line[x * 4 + 1] + padding);
					row[x * 3 + 2] = (byte)Math.Min(255, line[x * 4 + 2] + padding);
				}
				writer.Write(row);
			}
			return stream;
		}

		static MemoryStream EncodeToStream(this SKBitmap bitmap, string format, bool useAdvancedSettings)
		{
			var stream = UtilityService.CreateMemoryStream();
			try
			{
				if ("WebP".IsSameFormat(format))
				{
					using var pixmap = bitmap.PeekPixels();
					using var data = pixmap.Encode(new SKWebpEncoderOptions(SKWebpEncoderCompression.Lossy, useAdvancedSettings ? ImageExtensions.WebpQuality : ImageExtensions.WebpDefaultQuality)) ?? throw new InvalidOperationException("Cannot encode the image as WebP");
					data.SaveTo(stream);
				}
				else if ("Png".IsSameFormat(format))
				{
					using var image = SKImage.FromBitmap(bitmap);
					using var data = image.Encode(SKEncodedImageFormat.Png, 100) ?? throw new InvalidOperationException("Cannot encode the image as PNG");
					data.SaveTo(stream);
				}
				else if ("Bmp".IsSameFormat(format))
				{
					using var flattened = bitmap.AlphaType == SKAlphaType.Opaque ? null : bitmap.FlattenOnWhite();
					stream = ImageExtensions.EncodeBmp(flattened ?? bitmap);
				}
				else
				{
					using var flattened = bitmap.AlphaType == SKAlphaType.Opaque ? null : bitmap.FlattenOnWhite();
					using var image = SKImage.FromBitmap(flattened ?? bitmap);
					using var data = image.Encode(SKEncodedImageFormat.Jpeg, ImageExtensions.JpegQuality) ?? throw new InvalidOperationException("Cannot encode the image as JPEG");
					data.SaveTo(stream);
				}
				stream.Seek(0, SeekOrigin.Begin);
				return stream;
			}
			catch
			{
				stream.Dispose();
				throw;
			}
		}

		static Task<MemoryStream> EncodeToStreamAsync(this SKBitmap bitmap, string format, bool useAdvancedSettings, CancellationToken cancellationToken)
		{
			try
			{
				cancellationToken.ThrowIfCancellationRequested();
				return Task.FromResult(bitmap.EncodeToStream(format, useAdvancedSettings));
			}
			catch (Exception ex)
			{
				return Task.FromException<MemoryStream>(ex);
			}
		}

		/// <summary>
		/// Converts this image to other format
		/// </summary>
		/// <param name="imageStream"></param>
		/// <param name="format"></param>
		/// <param name="useAdvancedSettings"></param>
		/// <param name="resizeBigWebpImage"></param>
		/// <param name="cancellationToken"></param>
		/// <returns></returns>
		public static Task<MemoryStream> ConvertImageAsync(this Stream imageStream, string format, bool useAdvancedSettings, bool resizeBigWebpImage, CancellationToken cancellationToken = default)
		{
			using var bitmap = imageStream.DecodeBitmap();
			cancellationToken.ThrowIfCancellationRequested();
			if (useAdvancedSettings && resizeBigWebpImage && bitmap.Width > ImageExtensions.WebpMaxWidth && "WebP".IsSameFormat(format))
			{
				using var resized = bitmap.ResizeTo(ImageExtensions.GetFitSize(bitmap.Width, bitmap.Height, ImageExtensions.WebpMaxWidth, 0), true);
				return resized.EncodeToStreamAsync(format, useAdvancedSettings, cancellationToken);
			}
			return bitmap.EncodeToStreamAsync(format, useAdvancedSettings, cancellationToken);
		}

		/// <summary>
		/// Converts this image to other format
		/// </summary>
		/// <param name="imageStream"></param>
		/// <param name="format"></param>
		/// <param name="cancellationToken"></param>
		/// <returns></returns>
		public static Task<MemoryStream> ConvertImageAsync(this Stream imageStream, string format, CancellationToken cancellationToken = default)
			=> imageStream.ConvertImageAsync(format, true, ImageExtensions.ResizeBigWebpImage, cancellationToken);

		/// <summary>
		/// Converts this image to other format
		/// </summary>
		/// <param name="bytes"></param>
		/// <param name="format"></param>
		/// <param name="useAdvancedSettings"></param>
		/// <param name="resizeBigWebpImage"></param>
		/// <param name="cancellationToken"></param>
		/// <returns></returns>
		public static async Task<byte[]> ConvertImageAsync(this byte[] bytes, string format, bool useAdvancedSettings, bool resizeBigWebpImage, CancellationToken cancellationToken = default)
		{
			using var inputStream = bytes.ToMemoryStream();
			using var outputStream = await inputStream.ConvertImageAsync(format, useAdvancedSettings, resizeBigWebpImage, cancellationToken).ConfigureAwait(false);
			return outputStream.ToBytes();
		}

		/// <summary>
		/// Converts this image to other format
		/// </summary>
		/// <param name="bytes"></param>
		/// <param name="format"></param>
		/// <param name="useAdvancedSettings"></param>
		/// <param name="cancellationToken"></param>
		/// <returns></returns>
		public static Task<byte[]> ConvertImageAsync(this byte[] bytes, string format, bool useAdvancedSettings, CancellationToken cancellationToken = default)
			=> bytes.ConvertImageAsync(format, useAdvancedSettings, ImageExtensions.ResizeBigWebpImage, cancellationToken);

		/// <summary>
		/// Converts this image to other format
		/// </summary>
		/// <param name="bytes"></param>
		/// <param name="format"></param>
		/// <param name="cancellationToken"></param>
		/// <returns></returns>
		public static Task<byte[]> ConvertImageAsync(this byte[] bytes, string format, CancellationToken cancellationToken = default)
			=> bytes.ConvertImageAsync(format, true, cancellationToken);
	}
}
