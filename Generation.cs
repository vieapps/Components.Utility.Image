#region Related components
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using SkiaSharp;
#endregion

namespace net.vieapps.Components.Utility
{
	public static partial class ImageExtensions
	{
		/// <summary>
		/// Generates image
		/// </summary>
		/// <param name="bytes"></param>
		/// <param name="format"></param>
		/// <param name="width"></param>
		/// <param name="height"></param>
		/// <param name="asBig"></param>
		/// <param name="isWebP"></param>
		/// <param name="cancellationToken"></param>
		/// <returns></returns>
		public static async Task<byte[]> GenerateImageAsync(this byte[] bytes, string format, int width, int height, bool asBig, bool isWebP, CancellationToken cancellationToken = default)
		{
			if (width < 1 && height < 1)
				return isWebP && "WebP".IsSameFormat(format) ? bytes : await bytes.ConvertImageAsync(format, cancellationToken).ConfigureAwait(false);

			using var source = ImageExtensions.DecodeBitmap(bytes);
			cancellationToken.ThrowIfCancellationRequested();

			var size = ImageExtensions.GetFitSize(source.Width, source.Height, width, height);
			if ("WebP".IsSameFormat(format) && ImageExtensions.ResizeBigWebpImage && size.Width > ImageExtensions.WebpMaxWidth)
				size = ImageExtensions.GetFitSize(size.Width, size.Height, ImageExtensions.WebpMaxWidth, 0);

			cancellationToken.ThrowIfCancellationRequested();
			using var resized = size.Width == source.Width && size.Height == source.Height ? null : ImageExtensions.ResizeTo(source, size, asBig);

			cancellationToken.ThrowIfCancellationRequested();
			using var stream = await ImageExtensions.EncodeToStreamAsync(resized ?? source, format, true, cancellationToken).ConfigureAwait(false);
			return stream.ToBytes();
		}

		static SKBitmap GenerateBitmap(this Exception ex, int width, int height)
		{
			using var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Opaque));
			using (var canvas = new SKCanvas(bitmap))
			{
				canvas.Clear(SKColors.White);
				using var font = new SKFont(ImageExtensions.GetTypeface("Arial", true), 16);
				using var paint = new SKPaint { IsAntialias = true, Color = SKColors.Black };
				ImageExtensions.DrawTextAtTop(canvas, ex?.Message ?? "Unknown error", 20, 40, font, paint);
			}
			return bitmap;
		}

		/// <summary>
		/// Generates image (exception)
		/// </summary>
		/// <param name="ex"></param>
		/// <param name="width"></param>
		/// <param name="height"></param>
		/// <param name="format"></param>
		/// <returns></returns>
		public static byte[] GenerateImage(this Exception ex, int width, int height, string format = "WebP")
		{
			using var bitmap = ex.GenerateBitmap(width, height);
			using var stream = ImageExtensions.EncodeToStream(bitmap, format ?? "WebP", true);
			return stream.ToBytes();
		}

		/// <summary>
		/// Generates image (exception)
		/// </summary>
		/// <param name="ex"></param>
		/// <param name="width"></param>
		/// <param name="height"></param>
		/// <param name="format"></param>
		/// <param name="cancellationToken"></param>
		/// <returns></returns>
		public static async Task<byte[]> GenerateImageAsync(this Exception ex, int width, int height, string format = "WebP", CancellationToken cancellationToken = default)
		{
			using var bitmap = ex.GenerateBitmap(width, height);
			using var stream = await ImageExtensions.EncodeToStreamAsync(bitmap, format ?? "WebP", true, cancellationToken).ConfigureAwait(false);
			return stream.ToBytes();
		}

		static ConcurrentDictionary<string, SKTypeface> Typefaces { get; } = new ConcurrentDictionary<string, SKTypeface>(StringComparer.OrdinalIgnoreCase);

		static SKTypeface GetTypeface(string family, bool bold)
			=> ImageExtensions.Typefaces.GetOrAdd($"{family}|{bold}", _ => SKTypeface.FromFamilyName(family, bold ? SKFontStyle.Bold : SKFontStyle.Normal) ?? SKTypeface.Default);

		static void DrawTextAtTop(SKCanvas canvas, string text, float x, float top, SKFont font, SKPaint paint)
			=> canvas.DrawText(text, x, top - font.Metrics.Ascent, SKTextAlign.Left, font, paint);

		static SKColor[] ToColors(params string[] hexes)
			=> hexes.Select(SKColor.Parse).ToArray();

		static SKColor[] SmallCaptchaBackgroundColors { get; } = ImageExtensions.ToColors("#FFA500", "#D8BFD8", "#20B2AA", "#FFFF00", "#9ACD32", "#FFDEAD", "#FFFFFF");

		static SKColor[] BigCaptchaBackgroundColors { get; } = ImageExtensions.ToColors("#FFA500", "#D8BFD8", "#20B2AA", "#EE82EE", "#FFFF00", "#9ACD32", "#FFDEAD", "#D3D3D3", "#FF6347", "#90EE90", "#FFFFFF");

		static SKColor[] CaptchaFontColors { get; } = ImageExtensions.ToColors("#000000", "#0000FF", "#00008B", "#006400", "#FF00FF", "#FF0000", "#8B0000", "#000000", "#B22222", "#006400", "#9400D3");

		static string[] CaptchaFontNames { get; } = ["Verdana", "Arial", "Times New Roman", "Courier", "Courier New"];

		static SKBitmap GenerateBitmapBackround(int width, int height, SKColor[] colors)
		{
			var width1 = UtilityService.GetRandomNumber(UtilityService.GetRandomNumber(5, 10), UtilityService.GetRandomNumber(20, width / 2));
			var height1 = UtilityService.GetRandomNumber(UtilityService.GetRandomNumber(1, 10), UtilityService.GetRandomNumber(12, height));
			var width2 = UtilityService.GetRandomNumber(UtilityService.GetRandomNumber(15, width / 3), UtilityService.GetRandomNumber(width / 3, width / 2));
			var height2 = UtilityService.GetRandomNumber(UtilityService.GetRandomNumber(5, height / 4), UtilityService.GetRandomNumber(height / 4, height / 2));
			var width3 = UtilityService.GetRandomNumber(UtilityService.GetRandomNumber(width / 4, width / 2), UtilityService.GetRandomNumber(width / 2, width));
			var height3 = UtilityService.GetRandomNumber(UtilityService.GetRandomNumber(height / 5, height / 2), UtilityService.GetRandomNumber(height / 2, height));

			var background = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Opaque));
			try
			{
				using var canvas = new SKCanvas(background);
				canvas.Clear(colors[3]);

				using var paint = new SKPaint { Style = SKPaintStyle.Fill };
				paint.Color = colors[0];
				canvas.DrawRect(UtilityService.GetRandomNumber(0, width / 2), UtilityService.GetRandomNumber(0, height / 2), width1, height1, paint);
				paint.Color = colors[1];
				canvas.DrawRect(UtilityService.GetRandomNumber(width / 5, width / 2), UtilityService.GetRandomNumber(height / 5, height / 2), width2, height2, paint);
				paint.Color = colors[2];
				canvas.DrawRect(UtilityService.GetRandomNumber(width / 4, width / 3), UtilityService.GetRandomNumber(0, height / 3), width3, height3, paint);

				return background;
			}
			catch
			{
				background.Dispose();
				throw;
			}
		}

		static SKBitmap GenerateBitmap(this string code, bool isSmall = true)
		{
			var width = isSmall ? 110 : 220;
			var height = isSmall ? 24 : 48;
			var backgroundColors = isSmall ? ImageExtensions.SmallCaptchaBackgroundColors : ImageExtensions.BigCaptchaBackgroundColors;

			using var background = ImageExtensions.GenerateBitmapBackround(width, height,
			[
				backgroundColors[UtilityService.GetRandomNumber(0, backgroundColors.Length)],
				backgroundColors[UtilityService.GetRandomNumber(0, backgroundColors.Length)],
				backgroundColors[UtilityService.GetRandomNumber(0, backgroundColors.Length)],
				backgroundColors[UtilityService.GetRandomNumber(0, backgroundColors.Length)]
			]);

			using (var canvas = new SKCanvas(background))
			{
				if (isSmall)
				{
					using var linePaint = new SKPaint { IsAntialias = true, Color = SKColors.Gray };
					for (var index = 0; index < 6; index++)
					{
						linePaint.StrokeWidth = UtilityService.GetRandomNumber(1, 2);
						canvas.DrawLine(UtilityService.GetRandomNumber(0, width), UtilityService.GetRandomNumber(0, height), UtilityService.GetRandomNumber(0, width), UtilityService.GetRandomNumber(0, height), linePaint);
					}
				}

				using var textPaint = new SKPaint { IsAntialias = true };
				var step = 0;
				for (var index = 0; index < code.Length; index++)
				{
					using var font = new SKFont(ImageExtensions.GetTypeface(ImageExtensions.CaptchaFontNames[UtilityService.GetRandomNumber(0, ImageExtensions.CaptchaFontNames.Length)], true), isSmall ? 16 : 20);
					var x = index * 7 + step + UtilityService.GetRandomNumber(-1, 9);
					var y = UtilityService.GetRandomNumber(-2, 2);
					textPaint.Color = ImageExtensions.CaptchaFontColors[UtilityService.GetRandomNumber(0, ImageExtensions.CaptchaFontColors.Length)];

					var writtenCode = code.Substring(index, 1);
					if (writtenCode.Equals("I") || (UtilityService.GetRandomNumber() % 2 == 1 && !writtenCode.Equals("L")))
						writtenCode = writtenCode.ToLower();

					ImageExtensions.DrawTextAtTop(canvas, writtenCode, x, y, font, textPaint);
					step += UtilityService.GetRandomNumber(13, 23);
				}
			}

			var noiseColor = SKColor.Parse("#A9A9A9");
			for (var y = 0; y < height; y++)
				for (var x = 0; x < width; x++)
					if (x % 3 == 1 && y % 4 == 1)
						background.SetPixel(x, y, noiseColor);

			var divideTo = 64.0d + UtilityService.GetRandomNumber(1, 10);
			var distortion = isSmall ? UtilityService.GetRandomNumber(1, 5) : UtilityService.GetRandomNumber(5, 11);
			using var distorted = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Opaque));

			for (var y = 0; y < height; y++)
				for (var x = 0; x < width; x++)
				{
					var newX = (int)(x + distortion * Math.Sin(Math.PI * y / divideTo));
					var newY = (int)(y + distortion * Math.Cos(Math.PI * x / divideTo));
					if (newX < 0 || newX >= width)
						newX = 0;
					if (newY < 0 || newY >= height)
						newY = 0;
					distorted.SetPixel(x, y, background.GetPixel(newX, newY));
				}

			return distorted;
		}

		/// <summary>
		/// Generates image (captcha)
		/// </summary>
		/// <param name="code"></param>
		/// <param name="isSmall"></param>
		/// <param name="format"></param>
		/// <returns></returns>
		public static byte[] GenerateImage(this string code, bool isSmall = true, string format = "WebP")
		{
			using var bitmap = code.GenerateBitmap(isSmall);
			using var stream = ImageExtensions.EncodeToStream(bitmap, format, true);
			return stream.ToBytes();
		}

		/// <summary>
		/// Generates image (captcha)
		/// </summary>
		/// <param name="code"></param>
		/// <param name="isSmall"></param>
		/// <param name="format"></param>
		/// <returns></returns>
		public static async Task<byte[]> GenerateImageAsync(this string code, bool isSmall = true, string format = "WebP", CancellationToken cancellationToken = default)
		{
			using var bitmap = code.GenerateBitmap(isSmall);
			using var stream = await ImageExtensions.EncodeToStreamAsync(bitmap, format, true, cancellationToken).ConfigureAwait(false);
			return stream.ToBytes();
		}
	}
}
