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

            // Normalize the text (remove extra spaces, new lines, and common OCR noise like special chars)
            ocrText = Regex.Replace(ocrText, @"[\s\W]+", " ").Trim();

            // Fix OCR error for 'Bith' -> 'Birth'
            ocrText = ocrText.Replace("Bith", "Birth");

            // Extract Name (More robust handling for OCR issues)
            var nameMatch = Regex.Match(ocrText, @"(?i)(Name|Full\s*Name)[:\s]*([A-Za-z\s]+)", RegexOptions.IgnoreCase);
            if (nameMatch.Success)
            {
                extractedData["Name"] = nameMatch.Groups[2].Value.Trim();
            }

            // Extract Date of Birth (Look for variations of "Birth" including "8irth")
            var birthMatch = Regex.Match(ocrText, @"(?i)(Birth)[^0-9]*(\d{1,2}[A-Za-z]{3}\d{4}|\d{1,2}\s*[A-Za-z]+\s*\d{4})", RegexOptions.IgnoreCase);
            if (birthMatch.Success)
            {
                string dateText = birthMatch.Groups[2].Value.Trim();

                // Try parsing the extracted date text
                DateTime dob;
                if (DateTime.TryParse(dateText, out dob))
                {
                    extractedData["Date of Birth"] = dob.ToString("dd MMM yyyy");
                }
                else
                {
                    // Handle more complex date formats or fallback logic
                    string[] parts = dateText.Split(new[] { ' ', '/' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length == 3)
                    {
                        string day = parts[0];
                        string month = parts[1];
                        string year = parts[2];

                        // Validate and try parsing the date
                        if (DateTime.TryParse($"{day} {month} {year}", out dob))
                        {
                            extractedData["Date of Birth"] = dob.ToString("dd MMM yyyy");
                        }
                    }
                }
            }

            // Extract ID Number (Handle 10, 13, or 17 digit long ID numbers)
            var idMatch = Regex.Match(ocrText, @"(?i)(\d{10}|\d{13}|\d{17})", RegexOptions.IgnoreCase);
            if (idMatch.Success)
            {
                extractedData["ID Number"] = idMatch.Groups[1].Value.Trim();
            }

            // Extract Blood Group (Handle variations like "Blood group" and different formats of A, B, O, AB, +, -)
            var bloodGroupMatch = Regex.Match(ocrText, @"(?i)(Blood\s*Group|Blood\s*Type)[:\s]*([A|B|O|AB][+-]?)", RegexOptions.IgnoreCase);
            if (bloodGroupMatch.Success)
            {
                extractedData["Blood Group"] = bloodGroupMatch.Groups[2].Value.Trim();
            }

            // Handle fallback for missing ID numbers by extracting ID from the text if no specific label found
            if (!extractedData.ContainsKey("ID Number"))
            {
                var idFallbackMatch = Regex.Match(ocrText, @"(\d{10}|\d{13}|\d{17})", RegexOptions.IgnoreCase);
                if (idFallbackMatch.Success)
                {
                    extractedData["ID Number"] = idFallbackMatch.Groups[1].Value.Trim();
                }
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
