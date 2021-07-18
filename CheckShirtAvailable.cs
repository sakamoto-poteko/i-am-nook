using HtmlAgilityPack;
using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Logging;
using SendGrid;
using SendGrid.Helpers.Mail;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace iamnook
{
    public static class CheckShirtAvailable
    {
        private const string PRODUCT_URL = @"https://store-jp.nintendo.com/list/goods/fashion/NSJ_8_A2AAP.html";

        private const string SETTINGS_SIZE_IN_INTEREST = @"SIZE_IN_INTEREST";
        private const string SETTINGS_SENDGRID_API_KEY = @"SENDGRID_API_KEY";
        private const string SETTINGS_SENDGRID_RECIPIENT = @"MSG_RECEIPIENT";

        // every 24/6 hour, i.e., 0,6,12,18 o'clock
        [FunctionName("CheckShirtAvailable")]
        public static async Task Run(
            [TimerTrigger("0 0 */6 * * *", RunOnStartup = true)] TimerInfo timer,
            ILogger log)
        {
            log.LogInformation($"Start checking Nook's shirt at: {DateTime.Now}");

            var stockInformation = LoadStockInformation(log);

            var sizeInInterestStr = Environment.GetEnvironmentVariable(SETTINGS_SIZE_IN_INTEREST);
            var sizeInInterest = sizeInInterestStr?.Split(';', StringSplitOptions.RemoveEmptyEntries);

            if (sizeInInterest?.Any(size => stockInformation.ContainsKey(size) && stockInformation[size]) ?? false)
            {
                await SendEmailShirtAvailable(stockInformation, log);
            }
        }

        private static Dictionary<string, bool> LoadStockInformation(ILogger log)
        {
            var stockInformation = new Dictionary<string, bool>();

            HtmlWeb web = new HtmlWeb();
            var doc = web.Load(PRODUCT_URL);

            var xpathForProductList = @".//ul[contains(@class, ""productDetail--productType"")]/li[contains(@class, ""productDetail--type__item"")]";
            var lis = doc.DocumentNode.SelectNodes(xpathForProductList);
            if (lis.Count == 0)
            {
                return stockInformation;
            }

            var xpathForSize = @".//span[contains(@class, ""productDetail--type__name"")]";
            var xpathForPrice = @".//span[contains(@class, ""productDetail--type__price"")]";

            foreach (var li in lis)
            {
                var sizeElement = li.SelectSingleNode(xpathForSize);
                var priceElement = li.SelectSingleNode(xpathForPrice);

                if (sizeElement == null || priceElement == null)
                {
                    continue;
                }

                var size = sizeElement.InnerText;
                var inStock = !priceElement.InnerHtml.Contains("品切れ");

                stockInformation.Add(size, inStock);
            }

            return stockInformation;
        }

        private static async Task SendEmailShirtAvailable(Dictionary<string, bool> stockInformation, ILogger log)
        {
            var apiKey = Environment.GetEnvironmentVariable(SETTINGS_SENDGRID_API_KEY);
            var recipient = Environment.GetEnvironmentVariable(SETTINGS_SENDGRID_RECIPIENT);
            var recipients = recipient?.Split(';', StringSplitOptions.RemoveEmptyEntries) ?? new string[0];

            if (apiKey == null)
            {
                log.LogError("API key for SendGrid is not found. Not sending the mail.");
                return;
            }

            if (recipients.Length == 0)
            {
                log.LogError("No recipient provided. Not sending the mail.");
                return;
            }

            var client = new SendGridClient(apiKey);

            var stockDescriptions = stockInformation.Select(pair => $@"<p>Size {pair.Key} {(pair.Value ? "is" : "is <strong>not</strong>")} available</p>").ToList();
            var stockDescription = string.Join('\n', stockDescriptions);
            var msg = new SendGridMessage()
            {
                From = new EmailAddress("no-reply@afa.moe", "Nook's Shirt Notification Service"),
                Subject = "Nook's shirts restocked!",
                HtmlContent = $@"
                {stockDescription}
                <p>Visit this <a href=""https://store-jp.nintendo.com/list/goods/fashion/NSJ_8_A2AAP.html"">link</a> to buy!</p>"
            };

            foreach (var r in recipients)
            {
                msg.AddTo(new EmailAddress(recipient, recipient));
            }
            var response = await client.SendEmailAsync(msg);
            if (response.IsSuccessStatusCode)
            {
                log.LogInformation("Successfully sent the mail to recipients!");
            }
            else
            {
                log.LogError($"Failed to sent the mail to recipients: {await response.Body.ReadAsStringAsync()}");
            }
        }
    }
}
