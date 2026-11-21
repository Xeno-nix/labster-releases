using System.Net.Http.Json;
using CLMS.Application.Interfaces;
using Microsoft.Extensions.Configuration;

namespace CLMS.Infrastructure.Services;

public class EmailService : IEmailService
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;

    public EmailService(
        HttpClient httpClient,
        IConfiguration configuration)
    {
        _httpClient = httpClient;
        _configuration = configuration;
    }

    public async Task SendEmailAsync(
        string to,
        string subject,
        string body,
        CancellationToken cancellationToken = default)
    {
        var apiKey = _configuration["Brevo:ApiKey"];
        var senderEmail = _configuration["Brevo:SenderEmail"];
        var senderName = _configuration["Brevo:SenderName"];

        if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(senderEmail))
            throw new InvalidOperationException("Configure Brevo API key and sender before sending email.");

        var emailData = new
        {
            sender = new
            {
                name = string.IsNullOrWhiteSpace(senderName) ? "Labster" : senderName,
                email = senderEmail
            },

            to = new[]
            {
                new
                {
                    email = to
                }
            },

            subject = subject,

            htmlContent = body
        };


        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.brevo.com/v3/smtp/email");
        request.Headers.Add("api-key", apiKey);
        request.Content = JsonContent.Create(emailData);
        using var response = await _httpClient.SendAsync(request,cancellationToken);
        response.EnsureSuccessStatusCode();
    }
}
