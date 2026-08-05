using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MimeKit;
using CMSAPI.Application.Interfaces;

namespace CMSAPI.Application.Services;

// Sends CMS OTP emails via GoDaddy Workspace/Professional Email's SMTP relay
// (smtpout.secureserver.net) -- same MailKit-based approach easyHMSAPI's EmailService already
// uses for its own OTP emails, just a different mailbox/provider. Config keys mirror that
// project's Smtp:* convention for consistency across the two repos.
public class EmailService : IEmailService
{
    private readonly string _smtpServer;
    private readonly int _smtpPort;
    private readonly string _senderEmail;
    private readonly string _password;
    private readonly ILogger<EmailService> _logger;

    public EmailService(IConfiguration configuration, ILogger<EmailService> logger)
    {
        _smtpServer = configuration["Smtp:Server"] ?? "smtpout.secureserver.net";
        _smtpPort = int.TryParse(configuration["Smtp:Port"], out var port) ? port : 587;
        _senderEmail = configuration["Smtp:SenderEmail"] ?? string.Empty;
        _password = configuration["Smtp:Password"] ?? string.Empty;
        _logger = logger;
    }

    public async Task<bool> SendOtpEmailAsync(string recipientEmail, string otp, int expiryMinutes)
    {
        if (string.IsNullOrWhiteSpace(_senderEmail) || string.IsNullOrWhiteSpace(_password))
        {
            _logger.LogWarning("SendOtpEmailAsync skipped: Smtp:SenderEmail/Password not configured.");
            return false;
        }

        try
        {
            var email = new MimeMessage();
            email.From.Add(MailboxAddress.Parse(_senderEmail));
            email.To.Add(MailboxAddress.Parse(recipientEmail));
            email.Subject = "Your OTP Verification Code - NexEagle CMS";

            var builder = new BodyBuilder
            {
                HtmlBody = $@"
                    <div style='font-family: Arial, sans-serif; max-width: 600px; margin: 0 auto; padding: 20px;'>
                        <div style='background-color: #f8f9fa; padding: 20px; border-radius: 8px;'>
                            <h2 style='color: #0f52ba; margin-bottom: 20px;'>OTP Verification Code</h2>
                            <p style='font-size: 16px; color: #333; margin-bottom: 20px;'>
                                Your NexEagle CMS verification code is:
                            </p>
                            <div style='background-color: #0f52ba; color: white; padding: 15px; border-radius: 5px; text-align: center; margin: 20px 0;'>
                                <h1 style='margin: 0; font-size: 32px; letter-spacing: 5px;'>{otp}</h1>
                            </div>
                            <p style='font-size: 14px; color: #666; margin-top: 20px;'>
                                <strong>Important:</strong> This code will expire in {expiryMinutes} minutes.
                                NexEagle Support will never ask for this code. Do not share it with anyone.
                            </p>
                            <hr style='border: none; border-top: 1px solid #ddd; margin: 20px 0;'>
                            <p style='font-size: 12px; color: #999;'>
                                This is an automated message. Please do not reply to this email.
                            </p>
                        </div>
                    </div>",
            };
            email.Body = builder.ToMessageBody();

            using var smtp = new SmtpClient();
            await smtp.ConnectAsync(_smtpServer, _smtpPort, SecureSocketOptions.StartTls);
            await smtp.AuthenticateAsync(_senderEmail, _password);
            await smtp.SendAsync(email);
            await smtp.DisconnectAsync(true);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send OTP email to {Recipient}", recipientEmail);
            return false;
        }
    }
}
