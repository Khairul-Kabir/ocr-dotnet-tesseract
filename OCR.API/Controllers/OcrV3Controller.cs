using Microsoft.AspNetCore.Mvc;
using OpenCvSharp;
using OpenCvSharp.Extensions;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Tesseract;

namespace OCR.API.Controllers
{
    [Route("api/ocrv3")]
    [ApiController]
    public class OcrV3Controller : ControllerBase
    {
        [HttpPost]
        [Route("extract")]
        [Consumes("multipart/form-data")]
        public async Task<IActionResult> ExtractOcr([FromForm] OcrRequestDto request)
        {
            if (request.Image1 == null || request.Image2 == null)
                return BadRequest(new { message = "Both images must be provided." });

            try
            {
                Bitmap bitmap1 = await ConvertIFormFileToBitmapAsync(request.Image1);
                Bitmap bitmap2 = await ConvertIFormFileToBitmapAsync(request.Image2);

                // Enhance image without losing details
                Bitmap enhancedBitmap1 = ImproveImageQuality(bitmap1);
                Bitmap enhancedBitmap2 = ImproveImageQuality(bitmap2);

                // Extract text
                string text1 = ExtractTextFromImage(enhancedBitmap1);
                string text2 = ExtractTextFromImage(enhancedBitmap2);

                // Debug extracted text
                Console.WriteLine($"Extracted Text 1: {text1}");
                Console.WriteLine($"Extracted Text 2: {text2}");

                // Extract structured data
                var extractedData1 = ExtractRelevantData(text1);
                var extractedData2 = ExtractRelevantData(text2);

                return Ok(new
                {
                    Image1_Data = extractedData1,
                    Image2_Data = extractedData2,
                    Text1 = text1,
                    Text2 = text2,
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Error processing images.", error = ex.Message });
            }
        }

        private static async Task<Bitmap> ConvertIFormFileToBitmapAsync(IFormFile file)
        {
            using (var stream = new MemoryStream())
            {
                await file.CopyToAsync(stream);
                stream.Position = 0;
                using (var tempBitmap = new Bitmap(stream))
                {
                    return new Bitmap(tempBitmap);
                }
            }
        }

        private static Bitmap ImproveImageQuality(Bitmap bitmap)
        {
            Mat mat = bitmap.ToMat();

            // Convert to grayscale
            Mat gray = new Mat();
            Cv2.CvtColor(mat, gray, ColorConversionCodes.BGR2GRAY);

            // Apply CLAHE for contrast enhancement
            var clahe = Cv2.CreateCLAHE(2.0, new OpenCvSharp.Size(8, 8));
            clahe.Apply(gray, gray);

            // Apply Unsharp Masking (sharpen edges)
            Mat blurred = new Mat();
            Cv2.GaussianBlur(gray, blurred, new OpenCvSharp.Size(0, 0), 3);
            Cv2.AddWeighted(gray, 1.5, blurred, -0.5, 0, gray);

            // Save the improved image in the EnhancedImages folder
            Bitmap improvedBitmap = BitmapConverter.ToBitmap(gray);
            string directoryPath = Path.Combine(Directory.GetCurrentDirectory(), "EnhancedImages");
            if (!Directory.Exists(directoryPath))
            {
                Directory.CreateDirectory(directoryPath);
            }

            string savePath = Path.Combine(directoryPath, $"enhanced_{DateTime.Now:yyyyMMdd_HHmmss}.png");
            improvedBitmap.Save(savePath, System.Drawing.Imaging.ImageFormat.Jpeg);

            // Preserve original size
            return BitmapConverter.ToBitmap(gray);
        }

        private static string ExtractTextFromImage(Bitmap image)
        {
            string tessDataPath = Path.Combine(Directory.GetCurrentDirectory(), "tessdata-main");

            using (var engine = new TesseractEngine(tessDataPath, "eng", EngineMode.LstmOnly))  // Use LSTM OCR Model
            {
                engine.SetVariable("tessedit_char_whitelist", "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-:"); // Restrict unwanted characters
                engine.SetVariable("tessedit_pageseg_mode", "6"); // Treat as block of text

                using (var pix = Pix.LoadFromMemory(ConvertBitmapToByteArray(image)))
                {
                    using (var page = engine.Process(pix, PageSegMode.SparseText))  // Use SparseText mode for scattered words
                    {
                        return page.GetText().Trim();
                    }
                }
            }
        }

        private static byte[] ConvertBitmapToByteArray(Bitmap bitmap)
        {
            using (MemoryStream ms = new MemoryStream())
            {
                bitmap.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                return ms.ToArray();
            }
        }

        private static Dictionary<string, string> ExtractRelevantData(string ocrText)
        {
            var extractedData = new Dictionary<string, string>();

            ocrText = Regex.Replace(ocrText, @"[\s\W]+", " ").Trim();

            // Extract Name
            var nameMatch = Regex.Match(ocrText, @"(?i)(Name|Full\s*Name)[:\s]*([A-Za-z\s]+)");
            if (nameMatch.Success)
                extractedData["Name"] = nameMatch.Groups[2].Value.Trim();

            var birthMatch = Regex.Match(ocrText, @"(?i)(Birth|DOB)[^0-9]*(\d{1,2}[A-Za-z]{3}\d{4})");
            if (birthMatch.Success)
            {
                // Format the date as DDMMMYYYY (e.g., 02Mar1985)
                extractedData["Date of Birth"] = birthMatch.Groups[2].Value.Trim();
            }

            // Extract ID Number
            var idMatch = Regex.Match(ocrText, @"\b(\d{10}|\d{13}|\d{16}|\d{17})\b");
            if (idMatch.Success)
                extractedData["ID Number"] = idMatch.Groups[1].Value.Trim();

            return extractedData;
        }

    }
}
