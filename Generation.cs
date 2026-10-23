#region Related components
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Collections.Concurrent;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Drawing.Processing;
using SkiaSharp;
#endregion

namespace net.vieapps.Components.Utility
{
	public static partial class ServiceExtensions
	{

		#region Generate images
		internal static MemoryStream ToMemoryStream(this System.Drawing.Image image, System.Drawing.Imaging.ImageFormat format = null)
		{
			var stream = UtilityService.CreateMemoryStream();
			try
			{
				image.Save(stream, format ?? System.Drawing.Imaging.ImageFormat.Bmp);
				stream.Seek(0, SeekOrigin.Begin);
				return stream;
			}
			catch
			{
				stream.Dispose();
				throw;
			}
		}

		public static MemoryStream GenerateImageBySystemDrawing(this System.Drawing.Image image, int width, int height, bool asBig)
		{
			if (height < 1)
			{
				height = image.Height * width / image.Width;
				if (height < 1)
					height = image.Height;
			}
			else if (width < 1)
			{
				width = image.Width * height / image.Height;
				if (width < 1)
					width = image.Width;
			}

			if (asBig)
			{
				using var bitmap = new System.Drawing.Bitmap(width, height);
				using var graphics = System.Drawing.Graphics.FromImage(bitmap);
				graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
				graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
				graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
				graphics.DrawImage(image, new System.Drawing.Rectangle(0, 0, width, height));
				return bitmap.ToMemoryStream();
			}

			using var thumbnail = image.GetThumbnailImage(width, height, null, IntPtr.Zero);
			return thumbnail.ToMemoryStream();
		}

		public static MemoryStream GenerateImageByImageSharp(this System.Drawing.Image image, int width, int height, bool asBig)
		{
			if (height < 1)
			{
				height = image.Height * width / image.Width;
				if (height < 1)
					height = image.Height;
			}
			else if (width < 1)
			{
				width = image.Width * height / image.Height;
				if (width < 1)
					width = image.Width;
			}

			using var imageStream = image.ToMemoryStream();
			using var source = SixLabors.ImageSharp.Image.Load(imageStream);
			using var resized = source.Clone(context => context.Resize(new ResizeOptions { Mode = ResizeMode.Max, Size = new SixLabors.ImageSharp.Size(width, height), Sampler = asBig ? KnownResamplers.Lanczos3 : KnownResamplers.Bicubic }));

			var outputStream = UtilityService.CreateMemoryStream();
			try
			{
				resized.Save(outputStream, ServiceExtensions.JpegEncoder);
				outputStream.Seek(0, SeekOrigin.Begin);
				return outputStream;
			}
			catch
			{
				outputStream.Dispose();
				throw;
			}
		}

		public static MemoryStream GenerateImageBySkiaSharp(this System.Drawing.Image image, int width, int height, bool asBig)
		{
			using var imageStream = image.ToMemoryStream();
			using var source = ServiceExtensions.DecodeBitmap(imageStream);

			if (height < 1)
			{
				height = source.Height * width / source.Width;
				if (height < 1)
					height = source.Height;
			}
			else if (width < 1)
			{
				width = source.Width * height / source.Height;
				if (width < 1)
					width = source.Width;
			}

			var size = ServiceExtensions.GetFitSize(source.Width, source.Height, width, height);
			using var resized = ServiceExtensions.ResizeTo(source, size, asBig);
			return ServiceExtensions.EncodeToStream(resized, "Jpeg", true);
		}
		#endregion

		/// <summary>
		/// Generates an image by the selected generator
		/// </summary>
		/// <param name="image"></param>
		/// <param name="width"></param>
		/// <param name="height"></param>
		/// <param name="asBig"></param>
		/// <param name="generator"></param>
		/// <returns></returns>
		public static MemoryStream Generate(this System.Drawing.Image image, int width, int height, bool asBig, string generator = null)
			=> "ImageSharp".IsEquals(generator ?? ServiceExtensions.Generator)
				? image.GenerateImageByImageSharp(width, height, asBig)
				: "SkiaSharp".IsEquals(generator ?? ServiceExtensions.Generator)
					? image.GenerateImageBySkiaSharp(width, height, asBig)
					: image.GenerateImageBySystemDrawing(width, height, asBig);

		/// <summary>
		/// Generates an image by the selected generator
		/// </summary>
		public static async Task<byte[]> GenerateAsync(this byte[] bytes, string format, int width, int height, bool asBig, bool isWebP, CancellationToken cancellationToken, string generator = null)
		{
			if ("SkiaSharp".IsEquals(generator ?? ServiceExtensions.Generator))
			{
				if (width < 1 && height < 1)
					return isWebP && "WebP".IsSameFormat(format) ? bytes : await bytes.ConvertBySkiaSharpAsync(format, cancellationToken).ConfigureAwait(false);

				return await Task.Run(() =>
				{
					using var source = ServiceExtensions.DecodeBitmap(bytes);
					cancellationToken.ThrowIfCancellationRequested();

					var size = ServiceExtensions.GetFitSize(source.Width, source.Height, width, height);
					if ("WebP".IsSameFormat(format) && ServiceExtensions.ResizeBigWebpImage && size.Width > WebpMaxWidth)
						size = ServiceExtensions.GetFitSize(size.Width, size.Height, WebpMaxWidth, 0);

					using var resized = size.Width == source.Width && size.Height == source.Height ? null : ServiceExtensions.ResizeTo(source, size, asBig);
					using var stream = ServiceExtensions.EncodeToStream(resized ?? source, format, true);
					return stream.ToBytes();
				}, cancellationToken).ConfigureAwait(false);
			}

			if (width > 0 || height > 0)
			{
				using var inputStream = bytes.ToMemoryStream();
				using var imageStream = await inputStream.ConvertAsync("Bmp", cancellationToken, generator).ConfigureAwait(false);
				using var image = System.Drawing.Image.FromStream(imageStream);
				using var thumbnailStream = image.Generate(width, height, asBig, generator);
				using var outputStream = await thumbnailStream.ConvertAsync(format, cancellationToken, generator).ConfigureAwait(false);
				return outputStream.ToBytes();
			}

			return isWebP && "WebP".IsSameFormat(format) ? bytes : await bytes.ConvertAsync(format, cancellationToken, generator).ConfigureAwait(false);
		}

		public static Task<byte[]> GenerateAsync(this Exception ex, int width, int height, CancellationToken cancellationToken, string generator = null)
			=> "ImageSharp".IsEquals(generator ?? ServiceExtensions.Generator)
				? ex.GenerateImageByImageSharpAsync(width, height, cancellationToken)
				: "SkiaSharp".IsEquals(generator ?? ServiceExtensions.Generator)
					? ex.GenerateImageBySkiaSharpAsync(width, height, cancellationToken)
					: ex.GenerateImageBySystemDrawingAsync(width, height, cancellationToken);

		#region Generate images (exceptions)
		public static async Task<byte[]> GenerateImageBySystemDrawingAsync(this Exception ex, int width, int height, CancellationToken cancellationToken)
		{
			using var bitmap = new System.Drawing.Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format16bppRgb555);
			using var graphics = System.Drawing.Graphics.FromImage(bitmap);
			using var font = new System.Drawing.Font("Arial", 16, System.Drawing.FontStyle.Bold);
			graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
			graphics.Clear(System.Drawing.Color.White);
			graphics.DrawString(ex?.Message ?? "Unknown error", font, System.Drawing.SystemBrushes.WindowText, new System.Drawing.PointF(10, 40));

			using var bitmapStream = bitmap.ToMemoryStream();
			using var outputStream = await bitmapStream.ConvertAsync("WebP", cancellationToken).ConfigureAwait(false);
			return outputStream.ToBytes();
		}

		public static async Task<byte[]> GenerateImageByImageSharpAsync(this Exception ex, int width, int height, CancellationToken cancellationToken)
		{
			using var image = new SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32>(width, height, SixLabors.ImageSharp.Color.White);
			var font = SixLabors.Fonts.SystemFonts.CreateFont("Arial", 16, SixLabors.Fonts.FontStyle.Bold);
			image.Mutate(context => context.DrawText(ex?.Message ?? "Unknown error", font, SixLabors.ImageSharp.Color.Black, new SixLabors.ImageSharp.PointF(20, 40)));

			using var outputStream = UtilityService.CreateMemoryStream();
			await image.SaveAsync(outputStream, ServiceExtensions.WebpAdvancedEncoder, cancellationToken).ConfigureAwait(false);
			outputStream.Seek(0, SeekOrigin.Begin);
			return outputStream.ToBytes();
		}

		public static Task<byte[]> GenerateImageBySkiaSharpAsync(this Exception ex, int width, int height, CancellationToken cancellationToken)
			=> Task.Run(() =>
			{
				using var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Opaque));
				using (var canvas = new SKCanvas(bitmap))
				{
					canvas.Clear(SKColors.White);
					using var font = new SKFont(ServiceExtensions.GetTypeface("Arial", true), 16);
					using var paint = new SKPaint { IsAntialias = true, Color = SKColors.Black };
					ServiceExtensions.DrawTextAtTop(canvas, ex?.Message ?? "Unknown error", 20, 40, font, paint);
				}

				using var stream = ServiceExtensions.EncodeToStream(bitmap, "WebP", true);
				return stream.ToBytes();
			}, cancellationToken);
		#endregion

		#region Generate captchas
		public static MemoryStream GenerateCaptchaBySystemDrawing(this string code, bool isSmall = true)
		{
			var width = isSmall ? 110 : 220;
			var height = isSmall ? 24 : 48;
			var backgroundColors = isSmall
				? new[] { System.Drawing.Color.Orange, System.Drawing.Color.Thistle, System.Drawing.Color.LightSeaGreen, System.Drawing.Color.Yellow, System.Drawing.Color.YellowGreen, System.Drawing.Color.NavajoWhite, System.Drawing.Color.White }
				: [System.Drawing.Color.Orange, System.Drawing.Color.Thistle, System.Drawing.Color.LightSeaGreen, System.Drawing.Color.Violet, System.Drawing.Color.Yellow, System.Drawing.Color.YellowGreen, System.Drawing.Color.NavajoWhite, System.Drawing.Color.LightGray, System.Drawing.Color.Tomato, System.Drawing.Color.LightGreen, System.Drawing.Color.White];

			using var securityBitmap = ServiceExtensions.CreateCaptchaBackroundBySystemDrawing(width, height,
			[
				backgroundColors[UtilityService.GetRandomNumber(0, backgroundColors.Length)],
				backgroundColors[UtilityService.GetRandomNumber(0, backgroundColors.Length)],
				backgroundColors[UtilityService.GetRandomNumber(0, backgroundColors.Length)],
				backgroundColors[UtilityService.GetRandomNumber(0, backgroundColors.Length)]
			]);

			using var securityGraph = System.Drawing.Graphics.FromImage(securityBitmap);
			securityGraph.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

			if (!isSmall)
			{
				var noiseTexts = new List<string> { "Winners never quit", "Quitters never win", "Don't be evil", "Keep moving", "Connecting People", "Information at your fingertips", "No sacrifice no victory", "No pain no gain", "Where do you want to go today?", "Make business easier", "Simplify business process" };
				var noiseText = noiseTexts[UtilityService.GetRandomNumber(0, noiseTexts.Count)];
				noiseText += " " + noiseText + " " + noiseText + " " + noiseText;

				using var font1 = new System.Drawing.Font("Verdana", 10, System.Drawing.FontStyle.Underline);
				using var font2 = new System.Drawing.Font("Verdana", 12, System.Drawing.FontStyle.Bold);
				using var font3 = new System.Drawing.Font("Arial", 11, System.Drawing.FontStyle.Italic);
				using var font4 = new System.Drawing.Font("Arial", 12, System.Drawing.FontStyle.Bold);

				securityGraph.DrawString(noiseTexts[UtilityService.GetRandomNumber(0, noiseTexts.Count)] + " - " + noiseTexts[UtilityService.GetRandomNumber(0, noiseTexts.Count)], font1, System.Drawing.Brushes.White, new System.Drawing.PointF(0, 3));
				securityGraph.DrawString(noiseTexts[UtilityService.GetRandomNumber(0, noiseTexts.Count)] + " - " + noiseTexts[UtilityService.GetRandomNumber(0, noiseTexts.Count)], font2, System.Drawing.Brushes.White, new System.Drawing.PointF(5, 7));
				securityGraph.DrawString(noiseTexts[UtilityService.GetRandomNumber(0, noiseTexts.Count)] + " - " + noiseTexts[UtilityService.GetRandomNumber(0, noiseTexts.Count)], font3, System.Drawing.Brushes.White, new System.Drawing.PointF(-5, 20));
				securityGraph.DrawString(noiseText, font4, System.Drawing.Brushes.White, new System.Drawing.PointF(20, 28));
			}
			else
			{
				var randomIndex = UtilityService.GetRandomNumber(0, backgroundColors.Length);
				using (var noisePen = new System.Drawing.Pen(System.Drawing.Color.Gray, 2))
				{
					securityGraph.DrawLine(noisePen, new System.Drawing.Point(width, randomIndex), new System.Drawing.Point(randomIndex, height / 2 - randomIndex));
					securityGraph.DrawLine(noisePen, new System.Drawing.Point(width / 3 - randomIndex, randomIndex), new System.Drawing.Point(width / 2 + randomIndex, height - randomIndex));
				}

				using (var noisePen = new System.Drawing.Pen(System.Drawing.Color.Yellow, 1))
				{
					securityGraph.DrawLine(noisePen, new System.Drawing.Point(((width / 4) * 3) - randomIndex, randomIndex), new System.Drawing.Point(width / 3 + randomIndex, height - randomIndex));
					if (randomIndex % 2 == 1)
						securityGraph.DrawLine(noisePen, new System.Drawing.Point(width - randomIndex * 2, randomIndex), new System.Drawing.Point(randomIndex, height - randomIndex));
					else
						securityGraph.DrawLine(noisePen, new System.Drawing.Point(randomIndex, randomIndex), new System.Drawing.Point(width - randomIndex * 2, height - randomIndex));
				}

				randomIndex = UtilityService.GetRandomNumber(0, backgroundColors.Length);
				using (var noisePen = new System.Drawing.Pen(System.Drawing.Color.Magenta, 1))
				{
					securityGraph.DrawLine(noisePen, new System.Drawing.Point(((width / 6) * 3) - randomIndex, randomIndex), new System.Drawing.Point(width / 5 + randomIndex, height - randomIndex + 3));
					if (randomIndex % 2 == 1)
						securityGraph.DrawLine(noisePen, new System.Drawing.Point(width - randomIndex * 2, randomIndex - 1), new System.Drawing.Point(randomIndex, height - randomIndex - 3));
					else
						securityGraph.DrawLine(noisePen, new System.Drawing.Point(randomIndex, randomIndex + 1), new System.Drawing.Point(width - randomIndex * 2, height - randomIndex + 4));
				}

				randomIndex = UtilityService.GetRandomNumber(0, backgroundColors.Length);
				using (var noisePen = new System.Drawing.Pen(backgroundColors[UtilityService.GetRandomNumber(0, backgroundColors.Length)], 1))
				{
					securityGraph.DrawLine(noisePen, new System.Drawing.Point(((width / 10) * 3) - randomIndex, randomIndex), new System.Drawing.Point(width / 6 + randomIndex, height - randomIndex + 3));
					if (randomIndex % 2 == 1)
						securityGraph.DrawLine(noisePen, new System.Drawing.Point(width - randomIndex * 3, randomIndex - 2), new System.Drawing.Point(randomIndex, height - randomIndex - 2));
					else
						securityGraph.DrawLine(noisePen, new System.Drawing.Point(randomIndex, randomIndex + 2), new System.Drawing.Point(width - randomIndex * 3, height - randomIndex + 2));
				}
			}

			var fonts = new[] { "Verdana", "Arial", "Times New Roman", "Courier", "Courier New" };
			var fontColors = new[] { System.Drawing.Color.Black, System.Drawing.Color.Blue, System.Drawing.Color.DarkBlue, System.Drawing.Color.DarkGreen, System.Drawing.Color.Magenta, System.Drawing.Color.Red, System.Drawing.Color.DarkRed, System.Drawing.Color.Black, System.Drawing.Color.Firebrick, System.Drawing.Color.DarkGreen, System.Drawing.Color.Green, System.Drawing.Color.DarkViolet };

			if (isSmall)
			{
				var step = 0;
				for (var index = 0; index < code.Length; index++)
				{
					float x = index * 7 + step + UtilityService.GetRandomNumber(-1, 9);
					float y = UtilityService.GetRandomNumber(-2, 0);
					var writtenCode = code.Substring(index, 1);
					if (writtenCode.Equals("I") || (UtilityService.GetRandomNumber() % 2 == 1 && !writtenCode.Equals("L")))
						writtenCode = writtenCode.ToLower();

					var addedX = UtilityService.GetRandomNumber(-3, 5);
					using var font = new System.Drawing.Font(fonts[UtilityService.GetRandomNumber(0, fonts.Length)], UtilityService.GetRandomNumber(13, 19), System.Drawing.FontStyle.Bold);
					using var brush = new System.Drawing.SolidBrush(fontColors[UtilityService.GetRandomNumber(0, fontColors.Length)]);
					securityGraph.DrawString(writtenCode, font, brush, new System.Drawing.PointF(x + addedX, y));
					step += UtilityService.GetRandomNumber(13, 23);
				}
			}
			else
			{
				var step = 0;
				for (var index = 0; index < code.Length; index++)
				{
					var fontName = fonts[UtilityService.GetRandomNumber(0, fonts.Length)];
					float x = 2 + step, y = 10;
					step += 9;
					float fontSize = 15;

					if (index > 1 && index < 4)
					{
						fontSize = 25;
						x -= 10;
						y -= 5;
					}
					else if (index > 3 && index < 6)
					{
						y -= UtilityService.GetRandomNumber(3, 5);
						fontSize += index;
						step += index / 5;
						if (index == 4)
						{
							if (UtilityService.GetRandomNumber() % 2 == 1)
								y += UtilityService.GetRandomNumber(8, 12);
							else if (UtilityService.GetRandomNumber() % 2 == 2)
							{
								y -= UtilityService.GetRandomNumber(2, 6);
								fontSize += UtilityService.GetRandomNumber(1, 4);
							}
						}
					}
					else if (index > 5)
					{
						x += UtilityService.GetRandomNumber(0, 4);
						y -= UtilityService.GetRandomNumber(0, 4);
						fontSize += index - 7;
						step += index / 3 + 1;
						if (index == 10)
						{
							if (UtilityService.GetRandomNumber() % 2 == 1)
								y += UtilityService.GetRandomNumber(7, 14);
							else if (UtilityService.GetRandomNumber() % 2 == 2)
							{
								y -= UtilityService.GetRandomNumber(1, 3);
								fontSize += UtilityService.GetRandomNumber(2, 5);
							}
						}
					}

					var writtenCode = code.Substring(index, 1);
					if (writtenCode.Equals("I") || (UtilityService.GetRandomNumber() % 2 == 1 && !writtenCode.Equals("L")))
						writtenCode = writtenCode.ToLower();

					using var font = new System.Drawing.Font(fontName, fontSize, System.Drawing.FontStyle.Bold);
					using var brush1 = new System.Drawing.SolidBrush(fontColors[UtilityService.GetRandomNumber(0, fontColors.Length)]);
					using var brush2 = new System.Drawing.SolidBrush(fontColors[UtilityService.GetRandomNumber(0, fontColors.Length)]);
					securityGraph.DrawString(writtenCode, font, brush1, new System.Drawing.PointF(x + 2, y + 2));
					securityGraph.DrawString(writtenCode, font, brush2, new System.Drawing.PointF(x, y));
				}

				int maxX = width, maxY = height, startX = 0, startY = 0;
				var random = UtilityService.GetRandomNumber(1, 100);
				if (random > 80)
				{
					maxX -= maxX / 3;
					maxY /= 2;
				}
				else if (random > 60)
				{
					startX = maxX / 3;
					startY = maxY / 2;
				}
				else if (random > 30)
				{
					startX = maxX / 7;
					startY = maxY / 4;
					maxX -= maxX / 5;
					maxY -= maxY / 8;
				}

				for (var x = startX; x < maxX; x++)
					for (var y = startY; y < maxY; y++)
						if (x % 3 == 1 && y % 4 == 1)
							securityBitmap.SetPixel(x, y, System.Drawing.Color.DarkGray);
			}

			var divideTo = 64.0d + UtilityService.GetRandomNumber(1, 10);
			var distortion = isSmall ? UtilityService.GetRandomNumber(1, 5) : UtilityService.GetRandomNumber(5, 11);
			using var distorted = new System.Drawing.Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format16bppRgb555);

			for (var y = 0; y < height; y++)
				for (var x = 0; x < width; x++)
				{
					var newX = (int)(x + distortion * Math.Sin(Math.PI * y / divideTo));
					var newY = (int)(y + distortion * Math.Cos(Math.PI * x / divideTo));
					if (newX < 0 || newX >= width)
						newX = 0;
					if (newY < 0 || newY >= height)
						newY = 0;
					distorted.SetPixel(x, y, securityBitmap.GetPixel(newX, newY));
				}

			return distorted.ToMemoryStream();
		}

		static System.Drawing.Bitmap CreateCaptchaBackroundBySystemDrawing(int width, int height, System.Drawing.Color[] backgroundColors)
		{
			var width1 = UtilityService.GetRandomNumber(UtilityService.GetRandomNumber(5, 10), UtilityService.GetRandomNumber(20, width / 2));
			var height1 = UtilityService.GetRandomNumber(height / 4, height / 2);
			if (height > 20)
				height1 = UtilityService.GetRandomNumber(UtilityService.GetRandomNumber(1, 10), UtilityService.GetRandomNumber(12, height));

			using var bitmap1 = new System.Drawing.Bitmap(width1, height1, System.Drawing.Imaging.PixelFormat.Format16bppRgb555);
			using (var graphics = System.Drawing.Graphics.FromImage(bitmap1))
				graphics.Clear(backgroundColors[0]);

			var width2 = UtilityService.GetRandomNumber(UtilityService.GetRandomNumber(15, width / 3), UtilityService.GetRandomNumber(width / 3, width / 2));
			var height2 = UtilityService.GetRandomNumber(5, height / 3);
			if (height > 20)
				height2 = UtilityService.GetRandomNumber(UtilityService.GetRandomNumber(5, height / 4), UtilityService.GetRandomNumber(height / 4, height / 2));

			using var bitmap2 = new System.Drawing.Bitmap(width2, height2, System.Drawing.Imaging.PixelFormat.Format16bppRgb555);
			using (var graphics = System.Drawing.Graphics.FromImage(bitmap2))
				graphics.Clear(backgroundColors[1]);

			var width3 = UtilityService.GetRandomNumber(UtilityService.GetRandomNumber(width / 4, width / 2), UtilityService.GetRandomNumber(width / 2, width));
			var height3 = UtilityService.GetRandomNumber(height / 2, height);
			if (height > 20)
				height3 = UtilityService.GetRandomNumber(UtilityService.GetRandomNumber(height / 5, height / 2), UtilityService.GetRandomNumber(height / 2, height));

			using var bitmap3 = new System.Drawing.Bitmap(width3, height3, System.Drawing.Imaging.PixelFormat.Format16bppRgb555);
			using (var graphics = System.Drawing.Graphics.FromImage(bitmap3))
				graphics.Clear(backgroundColors[2]);

			var background = new System.Drawing.Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format16bppRgb555);
			try
			{
				using var graphics = System.Drawing.Graphics.FromImage(background);
				graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
				graphics.Clear(backgroundColors[3]);
				graphics.DrawImage(bitmap1, UtilityService.GetRandomNumber(0, width / 2), UtilityService.GetRandomNumber(0, height / 2));
				graphics.DrawImage(bitmap2, UtilityService.GetRandomNumber(width / 5, width / 2), UtilityService.GetRandomNumber(height / 5, height / 2));
				graphics.DrawImage(bitmap3, UtilityService.GetRandomNumber(width / 4, width / 3), UtilityService.GetRandomNumber(0, height / 3));
				return background;
			}
			catch
			{
				background.Dispose();
				throw;
			}
		}
		#endregion

		#region Generate captchas by ImageSharp
		public static MemoryStream GenerateCaptchaByImageSharp(this string code, bool isSmall = true)
		{
			var width = isSmall ? 110 : 220;
			var height = isSmall ? 24 : 48;
			var backgroundColors = isSmall
				? new[] { SixLabors.ImageSharp.Color.Orange, SixLabors.ImageSharp.Color.Thistle, SixLabors.ImageSharp.Color.LightSeaGreen, SixLabors.ImageSharp.Color.Yellow, SixLabors.ImageSharp.Color.YellowGreen, SixLabors.ImageSharp.Color.NavajoWhite, SixLabors.ImageSharp.Color.White }
				: [SixLabors.ImageSharp.Color.Orange, SixLabors.ImageSharp.Color.Thistle, SixLabors.ImageSharp.Color.LightSeaGreen, SixLabors.ImageSharp.Color.Violet, SixLabors.ImageSharp.Color.Yellow, SixLabors.ImageSharp.Color.YellowGreen, SixLabors.ImageSharp.Color.NavajoWhite, SixLabors.ImageSharp.Color.LightGray, SixLabors.ImageSharp.Color.Tomato, SixLabors.ImageSharp.Color.LightGreen, SixLabors.ImageSharp.Color.White];

			var fontColors = new[] { SixLabors.ImageSharp.Color.Black, SixLabors.ImageSharp.Color.Blue, SixLabors.ImageSharp.Color.DarkBlue, SixLabors.ImageSharp.Color.DarkGreen, SixLabors.ImageSharp.Color.Magenta, SixLabors.ImageSharp.Color.Red, SixLabors.ImageSharp.Color.DarkRed, SixLabors.ImageSharp.Color.Black, SixLabors.ImageSharp.Color.Firebrick, SixLabors.ImageSharp.Color.DarkGreen, SixLabors.ImageSharp.Color.DarkViolet };

			using var background = ServiceExtensions.CreateCaptchaBackroundByImageSharp(width, height,
			[
				backgroundColors[UtilityService.GetRandomNumber(0, backgroundColors.Length)],
				backgroundColors[UtilityService.GetRandomNumber(0, backgroundColors.Length)],
				backgroundColors[UtilityService.GetRandomNumber(0, backgroundColors.Length)],
				backgroundColors[UtilityService.GetRandomNumber(0, backgroundColors.Length)]
			]);

			if (isSmall)
				background.Mutate(context =>
				{
					for (var index = 0; index < 6; index++)
						context.DrawLine(SixLabors.ImageSharp.Color.Gray, UtilityService.GetRandomNumber(1, 2), [new SixLabors.ImageSharp.PointF(UtilityService.GetRandomNumber(0, width), UtilityService.GetRandomNumber(0, height)), new SixLabors.ImageSharp.PointF(UtilityService.GetRandomNumber(0, width), UtilityService.GetRandomNumber(0, height))]);
				});

			var fontNames = new[] { "Verdana", "Arial", "Times New Roman", "Courier", "Courier New" };
			var step = 0;

			for (var index = 0; index < code.Length; index++)
			{
				var fontName = fontNames[UtilityService.GetRandomNumber(0, fontNames.Length)];
				SixLabors.Fonts.Font font;
				try
				{
					font = SixLabors.Fonts.SystemFonts.CreateFont(fontName, isSmall ? 16 : 20, SixLabors.Fonts.FontStyle.Bold);
				}
				catch
				{
					font = SixLabors.Fonts.SystemFonts.CreateFont(SixLabors.Fonts.SystemFonts.Collection.Families.First().Name, isSmall ? 16 : 20, SixLabors.Fonts.FontStyle.Bold);
				}

				var x = index * 7 + step + UtilityService.GetRandomNumber(-1, 9);
				var y = UtilityService.GetRandomNumber(-2, 2);
				var color = fontColors[UtilityService.GetRandomNumber(0, fontColors.Length)];
				var writtenCode = code.Substring(index, 1);

				if (writtenCode.Equals("I") || (UtilityService.GetRandomNumber() % 2 == 1 && !writtenCode.Equals("L")))
					writtenCode = writtenCode.ToLower();

				background.Mutate(context => context.DrawText(writtenCode, font, color, new SixLabors.ImageSharp.PointF(x, y)));
				step += UtilityService.GetRandomNumber(13, 23);
			}

			background.ProcessPixelRows(accessor =>
			{
				for (var y = 0; y < height; y++)
				{
					var row = accessor.GetRowSpan(y);
					for (var x = 0; x < width; x++)
						if (x % 3 == 1 && y % 4 == 1)
							row[x] = SixLabors.ImageSharp.Color.DarkGray;
				}
			});

			var divideTo = 64.0d + UtilityService.GetRandomNumber(1, 10);
			var distortion = isSmall ? UtilityService.GetRandomNumber(1, 5) : UtilityService.GetRandomNumber(5, 11);
			using var distorted = new SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32>(width, height);

			for (var y = 0; y < height; y++)
				for (var x = 0; x < width; x++)
				{
					var newX = (int)(x + distortion * Math.Sin(Math.PI * y / divideTo));
					var newY = (int)(y + distortion * Math.Cos(Math.PI * x / divideTo));
					if (newX < 0 || newX >= width)
						newX = 0;
					if (newY < 0 || newY >= height)
						newY = 0;
					distorted[x, y] = background[newX, newY];
				}

			var stream = UtilityService.CreateMemoryStream();
			try
			{
				distorted.Save(stream, ServiceExtensions.JpegEncoder);
				stream.Seek(0, SeekOrigin.Begin);
				return stream;
			}
			catch
			{
				stream.Dispose();
				throw;
			}
		}

		static SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32> CreateCaptchaBackroundByImageSharp(int width, int height, SixLabors.ImageSharp.Color[] colors)
		{
			var width1 = UtilityService.GetRandomNumber(UtilityService.GetRandomNumber(5, 10), UtilityService.GetRandomNumber(20, width / 2));
			var height1 = UtilityService.GetRandomNumber(UtilityService.GetRandomNumber(1, 10), UtilityService.GetRandomNumber(12, height));
			var width2 = UtilityService.GetRandomNumber(UtilityService.GetRandomNumber(15, width / 3), UtilityService.GetRandomNumber(width / 3, width / 2));
			var height2 = UtilityService.GetRandomNumber(UtilityService.GetRandomNumber(5, height / 4), UtilityService.GetRandomNumber(height / 4, height / 2));
			var width3 = UtilityService.GetRandomNumber(UtilityService.GetRandomNumber(width / 4, width / 2), UtilityService.GetRandomNumber(width / 2, width));
			var height3 = UtilityService.GetRandomNumber(UtilityService.GetRandomNumber(height / 5, height / 2), UtilityService.GetRandomNumber(height / 2, height));

			using var bitmap1 = new SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32>(width1, height1, colors[0]);
			using var bitmap2 = new SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32>(width2, height2, colors[1]);
			using var bitmap3 = new SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32>(width3, height3, colors[2]);

			var background = new SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32>(width, height, colors[3]);
			try
			{
				background.Mutate(context =>
				{
					context.DrawImage(bitmap1, new SixLabors.ImageSharp.Point(UtilityService.GetRandomNumber(0, width / 2), UtilityService.GetRandomNumber(0, height / 2)), 1f);
					context.DrawImage(bitmap2, new SixLabors.ImageSharp.Point(UtilityService.GetRandomNumber(width / 5, width / 2), UtilityService.GetRandomNumber(height / 5, height / 2)), 1f);
					context.DrawImage(bitmap3, new SixLabors.ImageSharp.Point(UtilityService.GetRandomNumber(width / 4, width / 3), UtilityService.GetRandomNumber(0, height / 3)), 1f);
				});
				return background;
			}
			catch
			{
				background.Dispose();
				throw;
			}
		}
		#endregion

		#region Generate captchas by SkiaSharp
		static ConcurrentDictionary<string, SKTypeface> Typefaces { get; } = new ConcurrentDictionary<string, SKTypeface>(StringComparer.OrdinalIgnoreCase);

		static SKTypeface GetTypeface(string family, bool bold)
			=> ServiceExtensions.Typefaces.GetOrAdd($"{family}|{bold}", _ => SKTypeface.FromFamilyName(family, bold ? SKFontStyle.Bold : SKFontStyle.Normal) ?? SKTypeface.Default);

		static void DrawTextAtTop(SKCanvas canvas, string text, float x, float top, SKFont font, SKPaint paint)
			=> canvas.DrawText(text, x, top - font.Metrics.Ascent, SKTextAlign.Left, font, paint);

		static SKColor[] ToColors(params string[] hexes)
			=> hexes.Select(SKColor.Parse).ToArray();

		static SKColor[] SmallCaptchaBackgroundColors { get; } = ServiceExtensions.ToColors("#FFA500", "#D8BFD8", "#20B2AA", "#FFFF00", "#9ACD32", "#FFDEAD", "#FFFFFF");

		static SKColor[] BigCaptchaBackgroundColors { get; } = ServiceExtensions.ToColors("#FFA500", "#D8BFD8", "#20B2AA", "#EE82EE", "#FFFF00", "#9ACD32", "#FFDEAD", "#D3D3D3", "#FF6347", "#90EE90", "#FFFFFF");

		static SKColor[] CaptchaFontColors { get; } = ServiceExtensions.ToColors("#000000", "#0000FF", "#00008B", "#006400", "#FF00FF", "#FF0000", "#8B0000", "#000000", "#B22222", "#006400", "#9400D3");

		static string[] CaptchaFontNames { get; } = ["Verdana", "Arial", "Times New Roman", "Courier", "Courier New"];

		public static MemoryStream GenerateCaptchaBySkiaSharp(this string code, bool isSmall = true)
		{
			var width = isSmall ? 110 : 220;
			var height = isSmall ? 24 : 48;
			var backgroundColors = isSmall ? ServiceExtensions.SmallCaptchaBackgroundColors : ServiceExtensions.BigCaptchaBackgroundColors;

			using var background = ServiceExtensions.CreateCaptchaBackroundBySkiaSharp(width, height,
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
					using var font = new SKFont(ServiceExtensions.GetTypeface(ServiceExtensions.CaptchaFontNames[UtilityService.GetRandomNumber(0, ServiceExtensions.CaptchaFontNames.Length)], true), isSmall ? 16 : 20);
					var x = index * 7 + step + UtilityService.GetRandomNumber(-1, 9);
					var y = UtilityService.GetRandomNumber(-2, 2);
					textPaint.Color = ServiceExtensions.CaptchaFontColors[UtilityService.GetRandomNumber(0, ServiceExtensions.CaptchaFontColors.Length)];

					var writtenCode = code.Substring(index, 1);
					if (writtenCode.Equals("I") || (UtilityService.GetRandomNumber() % 2 == 1 && !writtenCode.Equals("L")))
						writtenCode = writtenCode.ToLower();

					ServiceExtensions.DrawTextAtTop(canvas, writtenCode, x, y, font, textPaint);
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

			return ServiceExtensions.EncodeToStream(distorted, "Jpeg", true);
		}

		static SKBitmap CreateCaptchaBackroundBySkiaSharp(int width, int height, SKColor[] colors)
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
		#endregion

		/// <summary>
		/// Generates captcha image
		/// </summary>
		/// <param name="code"></param>
		/// <param name="isSmall"></param>
		/// <param name="generator"></param>
		/// <returns></returns>
		public static MemoryStream Generate(this string code, bool isSmall = true, string generator = null)
			=> "ImageSharp".IsEquals(generator ?? ServiceExtensions.Generator)
				? code.GenerateCaptchaByImageSharp(isSmall)
				: "SkiaSharp".IsEquals(generator ?? ServiceExtensions.Generator)
					? code.GenerateCaptchaBySkiaSharp(isSmall)
					: code.GenerateCaptchaBySystemDrawing(isSmall);

	}
}
