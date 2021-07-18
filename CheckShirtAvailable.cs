using System;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;
using HtmlAgilityPack;
using System.Collections.Generic;
using SendGrid;
using SendGrid.Helpers.Mail;
using System.Linq;

namespace iamnook
{
    public static class CheckShirtAvailable
    {
        private const string PRODUCT_URL = @"https://store-jp.nintendo.com/list/goods/fashion/NSJ_8_A2AAP.html";

        // every 24/6 hour, i.e., 0,6,12,18 o'clock
        [FunctionName("CheckShirtAvailable")]
        public static async Task Run([TimerTrigger("0 0 */6 * * *")] TimerInfo timer, ILogger log)
        {
            log.LogInformation($"Start checking Nook's shirt at: {DateTime.Now}");

            var stockInformation = LoadStockInformation();
            stockInformation.Remove("130");

            if (stockInformation.Any(pair => pair.Value))
            {
                await SendEmailShirtAvailable(stockInformation);
            }
        }

        private static Dictionary<string, bool> LoadStockInformation()
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

            var xpathForSize = @".//li[contains(@class, ""productDetail--type__item"")]//span[contains(@class, ""productDetail--type__name"")]";
            var xpathForPrice = @".//li[contains(@class, ""productDetail--type__item"")]//span[contains(@class, ""productDetail--type__price"")]";

            foreach (var li in lis)
            {
                var sizeElement = li.SelectSingleNode(xpathForSize);
                var priceElement = li.SelectSingleNode(xpathForPrice);

                var size = sizeElement.InnerText;
                var inStock = !priceElement.InnerHtml.Contains("品切れ");

                stockInformation.Add(size, inStock);
            }

            return stockInformation;
        }

        private static async Task SendEmailShirtAvailable(Dictionary<string, bool> stockInformation)
        {
            var apiKey = Environment.GetEnvironmentVariable("SENDGRID_API_KEY");
            var recipient = Environment.GetEnvironmentVariable("MSG_RECEIPIENT");
            var recipients = recipient.Split(';');

            var client = new SendGridClient(apiKey);

            var stockDescriptions = stockInformation.Select(pair => $@"<p>Size {pair.Key} {(pair.Value ? "is" : "is <strong>not</strong>")} available</p>").ToList();
            var stockDescription = string.Join('\n', stockDescriptions);
            var msg = new SendGridMessage()
            {
                From = new EmailAddress("no-reply@afa.moe", "Nook's Shirt Notification Service"),
                Subject = "Nook's shirts are restocked!",
                PlainTextContent = "and easy to do anywhere, even with C#",
                HtmlContent = $@"
                {stockDescription}
                <p></p>
                <p>Visit this <a href=""https://store-jp.nintendo.com/list/goods/fashion/NSJ_8_A2AAP.html"">link</a> to buy!</p>"
            };

            foreach (var r in recipients)
            {
                msg.AddTo(new EmailAddress(recipient, recipient));
            }
            await client.SendEmailAsync(msg);
        }
    }
}
