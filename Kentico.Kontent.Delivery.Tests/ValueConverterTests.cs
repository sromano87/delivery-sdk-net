﻿using System;
using System.Linq;
using System.Reflection;
using NodaTime;
using Xunit;
using RichardSzalay.MockHttp;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using FakeItEasy;
using Kentico.Kontent.Delivery.Abstractions;
using Kentico.Kontent.Delivery.Abstractions.RetryPolicy;
using Kentico.Kontent.Delivery.StrongTyping;
using Kentico.Kontent.Delivery.Tests.Factories;
using Kentico.Kontent.Delivery.Abstractions.Models.Type.Element;

namespace Kentico.Kontent.Delivery.Tests
{
    [AttributeUsage(AttributeTargets.Property)]
    public class TestGreeterValueConverterAttribute : Attribute, IPropertyValueConverter
    {
        public object GetPropertyValue(PropertyInfo property, IContentElement elementData, ResolvingContext context)
        {
            return $"Hello {elementData.Value}!";
        }
    }


    [AttributeUsage(AttributeTargets.Property)]
    public class NodaTimeValueConverterAttribute : Attribute, IPropertyValueConverter
    {
        public object GetPropertyValue(PropertyInfo property, IContentElement elementData, ResolvingContext context)
        {
            var dt = DateTime.Parse(elementData.Value);
            if (dt != null)
            {
                var udt = DateTime.SpecifyKind(dt, DateTimeKind.Utc);
                ZonedDateTime zdt = ZonedDateTime.FromDateTimeOffset(udt);
                return zdt;
            }
            return null;
        }
    }


    public class ValueConverterTests
    {
        private readonly string guid;
        private readonly string baseUrl;

        public ValueConverterTests()
        {
            guid = Guid.NewGuid().ToString();
            baseUrl = $"https://deliver.kontent.ai/{guid}";
        }

        [Fact]
        public async void GreeterPropertyValueConverter()
        {
            var mockHttp = new MockHttpMessageHandler();
            string url = $"{baseUrl}/items/on_roasts";
            mockHttp.When(url).
               Respond("application/json", File.ReadAllText(Path.Combine(Environment.CurrentDirectory, $"Fixtures{Path.DirectorySeparatorChar}ContentLinkResolver{Path.DirectorySeparatorChar}on_roasts.json")));
            DeliveryClient client = InitializeDeliveryClient(mockHttp);

            var article = await client.GetItemAsync<Article>("on_roasts");

            Assert.Equal("Hello On Roasts!", article.Item.TitleConverted);
        }

        [Fact]
        public async void NodaTimePropertyValueConverter()
        {
            var mockHttp = new MockHttpMessageHandler();
            string url = $"{baseUrl}/items/on_roasts";
            mockHttp.When(url).
               Respond("application/json", File.ReadAllText(Path.Combine(Environment.CurrentDirectory, $"Fixtures{Path.DirectorySeparatorChar}ContentLinkResolver{Path.DirectorySeparatorChar}on_roasts.json")));
            DeliveryClient client = InitializeDeliveryClient(mockHttp);

            var article = await client.GetItemAsync<Article>("on_roasts");

            Assert.Equal(new ZonedDateTime(Instant.FromUtc(2014, 11, 7, 0, 0), DateTimeZone.Utc), article.Item.PostDateNodaTime);
        }

        [Fact]
        public async void RichTextViaValueConverter()
        {
            var mockHttp = new MockHttpMessageHandler();
            string url = $"{baseUrl}/items/coffee_beverages_explained";
            mockHttp.When(url).
               WithQueryString("depth=15").
               Respond("application/json", File.ReadAllText(Path.Combine(Environment.CurrentDirectory, $"Fixtures{Path.DirectorySeparatorChar}ContentLinkResolver{Path.DirectorySeparatorChar}coffee_beverages_explained.json")));
            DeliveryClient client = InitializeDeliveryClient(mockHttp);

            // Try to get recursive linked items on_roasts -> item -> on_roasts
            var article = await client.GetItemAsync<Article>("coffee_beverages_explained", new DepthParameter(15));

            var hostedVideo = article.Item.BodyCopyRichText.Blocks.FirstOrDefault(b => (b as IInlineContentItem)?.ContentItem is HostedVideo);
            var tweet = article.Item.BodyCopyRichText.Blocks.FirstOrDefault(b => (b as IInlineContentItem)?.ContentItem is Tweet);

            Assert.NotNull(hostedVideo);
            Assert.NotNull(tweet);
        }

        private DeliveryClient InitializeDeliveryClient(MockHttpMessageHandler mockHttp)
        {
            var deliveryHttpClient = new DeliveryHttpClient(mockHttp.ToHttpClient());
            var contentLinkUrlResolver = A.Fake<IContentLinkUrlResolver>();
            var deliveryOptions = DeliveryOptionsFactory.CreateMonitor(new DeliveryOptions { ProjectId = guid });
            var retryPolicy = A.Fake<IRetryPolicy>();
            var retryPolicyProvider = A.Fake<IRetryPolicyProvider>();
            A.CallTo(() => retryPolicyProvider.GetRetryPolicy()).Returns(retryPolicy);
            A.CallTo(() => retryPolicy.ExecuteAsync(A<Func<Task<HttpResponseMessage>>>._)).ReturnsLazily(c => c.GetArgument<Func<Task<HttpResponseMessage>>>(0)());
            var modelProvider = new ModelProvider(contentLinkUrlResolver, null, new CustomTypeProvider(), new PropertyMapper());
            var client = new DeliveryClient(deliveryOptions, modelProvider, retryPolicyProvider, null, deliveryHttpClient);

            return client;
        }
    }
}
