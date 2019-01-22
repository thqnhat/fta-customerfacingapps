#region using
using System;
using System.Configuration;
using System.Data.SqlClient;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Threading.Tasks;
using FunctionApp1.Models;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.WindowsAzure.Storage.Blob;
#endregion

namespace FunctionApp1
{
    public static class EventProcessorFunction
    {
        #region Run

        [FunctionName("EventProcessor")]
        public static async Task Run(
            [QueueTrigger("%App:StorageAccount:EventQueueName%", Connection = "App:StorageAccount:ConnectionString")]Event eventInfo,
            [Blob("tickets/ticket-{EntityId}.png", FileAccess.ReadWrite, Connection = "App:StorageAccount:ConnectionString")]CloudBlockBlob ticketImageBlob,
            TraceWriter log)
        {
            // Read more: https://azure.microsoft.com/en-us/blog/simplifying-security-for-serverless-and-web-apps-with-azure-functions-and-app-service/
            // Use KeyVault references feature to access to Azure Key Vault (it's not working in local)

            log.Info($"Received event type \"{eventInfo.EventType}\" for entity \"{eventInfo.EntityId}\"");
            log.Info($"QueueName read from Azure Key Vault: {GetAppConfiguration("App:StorageAccount:EventQueueName")}");

            if (string.Equals(eventInfo.EventType, "TicketCreated", StringComparison.OrdinalIgnoreCase))
            {
                var sqlDatabaseConnectionString = ConfigurationManager.AppSettings["App:SqlDatabase:ConnectionString"];
                if (!string.IsNullOrWhiteSpace(sqlDatabaseConnectionString))
                {
                    await CreateTicketImageAsync(sqlDatabaseConnectionString, int.Parse(eventInfo.EntityId), ticketImageBlob, log);
                }
            }
        }

        #endregion

        #region Create Ticket Image
        private static async Task CreateTicketImageAsync(string sqlDatabaseConnectionString, int ticketId, CloudBlockBlob ticketImageBlob, TraceWriter log)
        {
            // Ensure the blob container is created.
            await ticketImageBlob.Container.CreateIfNotExistsAsync();

            using (var connection = new SqlConnection(sqlDatabaseConnectionString))
            {
                // Retrieve the ticket from the database.
                log.Info($"Retrieving details for ticket \"{ticketId}\" from SQL Database...");
                await connection.OpenAsync();
                var getTicketCommand = connection.CreateCommand();
                getTicketCommand.CommandText = "SELECT Concerts.Artist, Concerts.Location, Concerts.StartTime, Concerts.Price, Users.DisplayName FROM Tickets INNER JOIN Concerts ON Tickets.ConcertId = Concerts.Id INNER JOIN Users ON Tickets.UserId = Users.Id WHERE Tickets.Id = @id";
                getTicketCommand.Parameters.Add(new SqlParameter("id", ticketId));
                using (var ticketDataReader = await getTicketCommand.ExecuteReaderAsync())
                {
                    // Get ticket details.
                    await ticketDataReader.ReadAsync();
                    var artist = ticketDataReader.GetString(0);
                    var location = ticketDataReader.GetString(1);
                    var startTime = ticketDataReader.GetDateTimeOffset(2);
                    var price = ticketDataReader.GetDouble(3);
                    var userName = ticketDataReader.GetString(4);

                    // Generate the ticket image.
                    using (var headerFont = new Font("Arial", 18, FontStyle.Bold))
                    using (var textFont = new Font("Arial", 12, FontStyle.Regular))
                    using (var bitmap = new Bitmap(640, 200, PixelFormat.Format24bppRgb))
                    using (var graphics = Graphics.FromImage(bitmap))
                    using (var outputStream = ticketImageBlob.OpenWrite())
                    {
                        graphics.SmoothingMode = SmoothingMode.AntiAlias;
                        graphics.Clear(Color.White);

                        // Print concert details.
                        graphics.DrawString(artist, headerFont, Brushes.DarkSlateBlue, new PointF(10, 10));
                        graphics.DrawString($"{location}   |   {startTime.UtcDateTime.ToString()}", textFont, Brushes.Gray, new PointF(10, 40));
                        graphics.DrawString($"{userName}   |   {price.ToString("c")}", textFont, Brushes.Gray, new PointF(10, 60));

                        // Print a fake barcode.
                        var random = new Random();
                        var offset = 15;
                        while (offset < 620)
                        {
                            var width = 2 * random.Next(1, 3);
                            graphics.FillRectangle(Brushes.Black, offset, 90, width, 90);
                            offset += width + (2 * random.Next(1, 3));
                        }

                        // Save to blob storage.
                        log.Info("Uploading image to blob storage...");
                        bitmap.Save(outputStream, ImageFormat.Png);
                    }
                }

                // Update the ticket in the database with the image URL.
                var policy = new SharedAccessBlobPolicy { Permissions = SharedAccessBlobPermissions.Read, SharedAccessExpiryTime = DateTimeOffset.MaxValue };
                var imageUrl = ticketImageBlob.Uri.ToString() + ticketImageBlob.GetSharedAccessSignature(policy);
                log.Info($"Updating ticket with image URL {imageUrl}...");
                var updateTicketCommand = connection.CreateCommand();
                updateTicketCommand.CommandText = "UPDATE Tickets SET ImageUrl=@imageUrl WHERE Id=@id";
                updateTicketCommand.Parameters.Add(new SqlParameter("id", ticketId));
                updateTicketCommand.Parameters.Add(new SqlParameter("imageUrl", imageUrl));
                await updateTicketCommand.ExecuteNonQueryAsync();
            }
        }

        private static string GetAppConfiguration(string name)
        {
            return Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.Process);
        }
        #endregion
    }
}
