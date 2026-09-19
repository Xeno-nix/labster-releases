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
        string body)
    {
        var apiKey = _configuration["Brevo:ApiKey"];
        var senderEmail = _configuration["Brevo:SenderEmail"];
        var senderName = _configuration["Brevo:SenderName"];

        _httpClient.DefaultRequestHeaders.Clear();
        _httpClient.DefaultRequestHeaders.Add(
            "api-key",
            apiKey);

        var emailData = new
        {
            sender = new
            {
                name = senderName,
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

        var response = await _httpClient.PostAsJsonAsync(
            "https://api.brevo.com/v3/smtp/email",
            emailData);

        response.EnsureSuccessStatusCode();
    }
}