using Microsoft.AspNetCore.Mvc;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Tesseract;

namespace OCR.API.Controllers
{
    [Route("api/ocrv2")]
    [ApiController]
    public class OcrV2Controller : ControllerBase
    {
        [HttpPost]
        [Route("extract")]
        [Consumes("multipart/form-data")]
        public async Task<IActionResult> ExtractOcr([FromForm] OcrRequestDto request)
        {
            if (request.Image1 == null || request.Image2 == null)
                return BadRequest(new { message = "Invalid request. Both images must be provided." });

            try
            {
                Bitmap bitmap1 = await ConvertIFormFileToBitmapAsync(request.Image1);
                Bitmap bitmap2 = await ConvertIFormFileToBitmapAsync(request.Image2);

                string text1 = ExtractTextFromImage(bitmap1);
                string text2 = ExtractTextFromImage(bitmap2);

                // Extract relevant data
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
                return StatusCode(500, new { message = "An error occurred while processing the images.", error = ex.Message });
            }
        }

        private static async Task<Bitmap> ConvertIFormFileToBitmapAsync(IFormFile file)
        {
            using (var stream = new MemoryStream())
            {
                await file.CopyToAsync(stream);
                stream.Position = 0; // Reset stream position
                using (var tempBitmap = new Bitmap(stream))
                {
                    return new Bitmap(tempBitmap); // Create new Bitmap to avoid GDI+ errors
                }
            }
        }

        private static string ExtractTextFromImage(Bitmap image)
        {
            string tessDataPath = Path.Combine(Directory.GetCurrentDirectory(), "tessdata-main");

            using (var engine = new TesseractEngine(tessDataPath, "eng", EngineMode.Default))
            {
                using (MemoryStream ms = new MemoryStream())
                {
                    try
                    {
                        Bitmap newImage = new Bitmap(image);
                        newImage.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                        newImage.Dispose(); // Dispose copied image
                    }
                    catch (ExternalException ex)
                    {
                        throw new InvalidOperationException("Failed to save the image to the memory stream.", ex);
                    }

                    byte[] imageBytes = ms.ToArray();

                    using (var pix = Pix.LoadFromMemory(imageBytes))
                    {
                        using (var page = engine.Process(pix))
                        {
                            return page.GetText().Trim();
                        }
                    }
                }
            }
        }

        private static Dictionary<string, string> ExtractRelevantData(string ocrText)
        {
            var extractedData = new Dictionary<string, string>();

            // Extract Name
            var nameMatch = Regex.Match(ocrText, @"\bName\s*\n(.+)", RegexOptions.IgnoreCase);
            if (nameMatch.Success)
            {
                extractedData["Name"] = nameMatch.Groups[1].Value.Trim();
            }

            // Extract Blood Group
            var bloodGroupMatch = Regex.Match(ocrText, @"Blood Group:\s*([A|B|O|AB][+-])", RegexOptions.IgnoreCase);
            if (bloodGroupMatch.Success)
            {
                extractedData["Blood Group"] = bloodGroupMatch.Groups[1].Value.Trim();
            }

            // Extract Date of Birth
            var dobMatch = Regex.Match(ocrText, @"Date\s*of\s*Birth\s*(\d{2}\s\w{3}\s\d{4})", RegexOptions.IgnoreCase);
            if (dobMatch.Success)
            {
                extractedData["Date of Birth"] = dobMatch.Groups[1].Value.Trim();
            }

            // Extract ID Number
            var idMatch = Regex.Match(ocrText, @"\b(\d{10})\b");
            if (idMatch.Success)
            {
                extractedData["ID Number"] = idMatch.Groups[1].Value.Trim();
            }

            return extractedData;
        }
    }

    public class OcrRequestDto
    {
        public IFormFile Image1 { get; set; }
        public IFormFile Image2 { get; set; }
    }
}
