using System;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace RevertFacultyStatus
{
    public class RevertFacultyStatus
    {
        private readonly ILogger _logger;
        private const int MAX_IDLE_TIME_SECONDS = 60;
        public static CosmosClient cosmosClient;
        public static IConfiguration config;

        public RevertFacultyStatus(ILoggerFactory loggerFactory)
        {
            _logger = loggerFactory.CreateLogger<RevertFacultyStatus>();
        }

        [Function("RevertFacultyStatus")]
        public static async Task Run(
            [TimerTrigger("0 */1 * * * *")] TimerInfo timer, FunctionContext con)
        {
            var logger = con.GetLogger("CheckFacultyStatus");

            try
            {
                var config = new ConfigurationBuilder()
                    .SetBasePath(Directory.GetCurrentDirectory())
                    .AddJsonFile("localsettings.json", optional: true, reloadOnChange: true)
                    .AddEnvironmentVariables()
                    .Build();

                var cs = config["connectionString"];


                cosmosClient = new CosmosClient(cs);

                // Query the Cosmos DB database for faculty members whose last seen timestamp is older than the threshold
                var databaseName = "FacultyAvailabilityDB";
                var containerName = "FacultyAvailabilityCollection";
                var container = cosmosClient.GetContainer(databaseName, containerName);
                var query = new QueryDefinition("SELECT * FROM c WHERE c.status = @status AND " +
                    "(IS_NULL(c.lastSeenTimestamp) OR c.lastSeenTimestamp <= @threshold)")
                    .WithParameter("@status", "available")
                    .WithParameter("@threshold", DateTime.UtcNow.AddSeconds(-MAX_IDLE_TIME_SECONDS).ToString("o"));
                var iterator = container.GetItemQueryIterator<JObject>(query);
                var results = new List<JObject>();
                while (iterator.HasMoreResults)
                {
                    var response = await iterator.ReadNextAsync();
                    results.AddRange(response);
                }

                // Update the status of faculty members whose last seen timestamp is older than the threshold
                foreach (var faculty in results)
                {
                    faculty["status"] = "unavailable";
                    await container.ReplaceItemAsync(faculty, faculty["id"].ToString());
                    logger.LogInformation($"Updated status to unavailable for faculty with id: {faculty["id"]}");
                }
            }
            catch (Exception ex)
            {
                logger.LogError($"Error checking faculty status: {ex.Message}");
            }
        }
    }


}