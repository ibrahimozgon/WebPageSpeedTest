﻿using System;
using System.Collections.Generic;
using System.Configuration;
using System.Globalization;
using System.Linq;
using System.Net.Mail;
using log4net;
using NUnit.Framework;
using OpenQA.Selenium;

namespace WebPageTest
{
    [TestFixture]
    public class WebPageTestRun
    {
        private ILog _log;
        private const string WebpageTestUrl = "https://www.webpagetest.org/";

        [SetUp]
        public void SetUp()
        {
            log4net.Config.XmlConfigurator.Configure();
            _log = LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);
            _log.WarnFormat("Selenium Basladi");
        }

        [TearDown]
        public void TearDown()
        {
            _log.WarnFormat("Selenium Sonlandi");
        }

        [Test]
        public void Run()
        {
            var results = new List<PerformanceTestResult>();
            results.AddRange(RunPagesTests("urls", false));
            results.AddRange(RunPagesTests("mobileUrls", true));
            SendMail(results);
        }

        private IEnumerable<PerformanceTestResult> RunPagesTests(string key, bool isMobile)
        {
            var urlStr = ConfigurationManager.AppSettings.Get(key);
            if (!string.IsNullOrEmpty(urlStr))
            {
                return urlStr.Split(';')
                    .Where(s => !string.IsNullOrEmpty(s))
                    .Select(s => TestPage(s, isMobile)).ToList();
            }
            return new List<PerformanceTestResult>();
        }

        private PerformanceTestResult TestPage(string url, bool isMobile)
        {
            var commands = new Commands();
            try
            {
                commands.OpenDriver(WebpageTestUrl);
                commands.GoTo(WebpageTestUrl);
                _log.WarnFormat("Browser acildi.  Url: {0}  -  IsMobile: {1}", url, isMobile);
                FillPageForm(commands, url, isMobile);
                var result = GetTestResult(commands);
                result.TestedPageUrl = url;
                result.Date = DateTime.Now.ToString(CultureInfo.InvariantCulture);

                _log.WarnFormat("Browser kapatildi.");
                return result;
            }
            catch (Exception e)
            {
                _log.ErrorFormat("Test sirasinda hata alindi. Url: {0}  -  IsMobile: {1}  -  Hata: {2}", url, isMobile,
                    e);
                return null;
            }
            finally
            {
                commands.Dispose();
            }
        }

        private static void FillPageForm(Commands commands, string url, bool isMobile)
        {
            var whereValue = ConfigurationManager.AppSettings.Get("whereValue");
            var locationValue = ConfigurationManager.AppSettings.Get("locationValue");
            const string urlInput = "//input[@name='url']";
            commands.SendKeys(LocatorType.XPath, urlInput, url);
            commands.Click(LocatorType.Id, "advanced_settings");
            commands.SelectDropDownElement(By.Name("where"), DropdownSelector.Value, whereValue);
            commands.SelectDropDownElement(By.Name("location"), DropdownSelector.Value, locationValue);
            commands.SendKeys(LocatorType.Id, "number_of_tests", "1");
            if (isMobile)
            {
                var chromeTabXPath = "//div[@id='test_subbox-container']//ul//li[3]";
                commands.WaitForVisible(LocatorType.XPath, chromeTabXPath);
                commands.Click(LocatorType.XPath, chromeTabXPath);
                commands.Click(LocatorType.Id, "mobile");
                commands.SelectDropDownElement(By.Name("mobileDevice"), DropdownSelector.Value, "GalaxyS7");
            }
            var blockedDomains = ConfigurationManager.AppSettings["blockedDomains"];
            if (!string.IsNullOrEmpty(blockedDomains))
            {
                const string blockXPath = "//div[@id='test_subbox-container']//ul//li[6]";
                commands.Click(LocatorType.XPath, blockXPath);
                commands.SendKeys(LocatorType.Id, "block_requests_containing", blockedDomains);
            }

            commands.Click(LocatorType.ClassName, "start_test");
        }

        private static PerformanceTestResult GetTestResult(Commands commands)
        {
            var timeoutTimespan = TimeSpan.FromMinutes(10);
            var timeout = ConfigurationManager.AppSettings["timeout"];
            if (!string.IsNullOrEmpty(timeout))
                timeoutTimespan = TimeSpan.FromMinutes(int.Parse(timeout));

            commands.WaitForVisible(LocatorType.XPath, "//div[@id='optimization']", timeoutTimespan);
            var firstViewTime = commands.GetText(LocatorType.XPath, "//td[@id='LoadTime']");
            var from = commands.GetText(LocatorType.ClassName, "heading_details");
            if (!string.IsNullOrEmpty(from))
                from = from.Replace("From: ", "");
            var url = commands.GetDriverUrl();
            var screenshotPath = commands.TakeScreenshotAndGetPath();
            return new PerformanceTestResult
            {
                ResultUrl = url,
                FirstViewTime = firstViewTime,
                ScreenshotPath = screenshotPath,
                From = from,
            };
        }

        private static void SendMail(IEnumerable<PerformanceTestResult> results)
        {
            var mail = new MailMessage();
            var smtpServer = new SmtpClient(ConfigurationManager.AppSettings["mail.smtp"]);
            mail.From = new MailAddress(ConfigurationManager.AppSettings["mail.from"]);
            mail.IsBodyHtml = true;
            var emailList = ConfigurationManager.AppSettings["mail.to"];

            foreach (var email in emailList.Split(';'))
            {
                mail.To.Add(email);
            }

            mail.Subject = "Webpage Hız Testi";
            foreach (var result in results.Where(t => t != null))
            {
                var screenshotAttachment = new Attachment(result.ScreenshotPath);
                mail.Attachments.Add(screenshotAttachment);

                mail.Body += $"Test edilen Sayfa: <a href=\"{result.TestedPageUrl}\">{result.TestedPageUrl}</a>" +
                             $"<br>Lokasyon:{result.From}" +
                             $"<br>Sonuş sayfası: <a href=\"{result.ResultUrl}\">{result.ResultUrl}</a>" +
                             $"<br>Saat: {result.Date}" +
                             $"<br>Süre: {result.FirstViewTime}<br><hr><br>";
            }

            smtpServer.Port = int.Parse(ConfigurationManager.AppSettings["mail.port"]);
            smtpServer.Credentials = new System.Net.NetworkCredential(ConfigurationManager.AppSettings["mail.from"],
                ConfigurationManager.AppSettings["mail.password"]);
            smtpServer.EnableSsl = ConfigurationManager.AppSettings["mail.ssl"] == "1";

            smtpServer.Send(mail);
        }
    }
}
