using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Linq;
using MetroMapGenerator.Core.Models;
using CsvHelper;
using CsvHelper.Configuration;
using System.Globalization;

namespace MetroMapGenerator.Processing.GTFS
{
    public class GTFSReader
    {
        private const int BUFFER_SIZE = 65536; // 64KB buffer
        private readonly CsvConfiguration _csvConfig;

        public GTFSReader()
        {
            _csvConfig = new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                HasHeaderRecord = true,
                HeaderValidated = null,
                MissingFieldFound = null,
                IgnoreBlankLines = true,
                TrimOptions = TrimOptions.Trim,
                DetectDelimiter = true,
                Mode = CsvMode.RFC4180,
                ReadingExceptionOccurred = args => true, // Continue on errors
                ShouldSkipRecord = args => false, // Process all records
                PrepareHeaderForMatch = args => args.Header.ToLower().Trim(),
                CacheFields = true
            };
        }

        public async Task<IEnumerable<Route>> ReadRoutesAsync(string path)
        {
            return await ReadCsvFileAsync<Route, RouteMap>(path);
        }

        public async Task<IEnumerable<Stop>> ReadStopsAsync(string path)
        {
            return await ReadCsvFileAsync<Stop, StopMap>(path);
        }

        public async Task<IEnumerable<Trip>> ReadTripsAsync(string path)
        {
            return await ReadCsvFileAsync<Trip, TripMap>(path);
        }

        public async Task<IEnumerable<StopTime>> ReadStopTimesAsync(string path)
        {
            return await ReadCsvFileAsync<StopTime, StopTimeMap>(path);
        }

        private async Task<IEnumerable<T>> ReadCsvFileAsync<T, TMap>(string path) 
            where T : class, new() 
            where TMap : ClassMap<T>
        {
            var results = new List<T>();
            
            await Task.Run(() =>
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, BUFFER_SIZE);
                using var reader = new StreamReader(stream, bufferSize: BUFFER_SIZE);
                using var csv = new CsvReader(reader, _csvConfig);

                // Register the mapping configuration
                csv.Context.RegisterClassMap<TMap>();

                // Read header only once
                csv.Read();
                csv.ReadHeader();

                // Process records in batches
                const int batchSize = 1000;
                var batch = new List<T>(batchSize);

                while (csv.Read())
                {
                    try
                    {
                        var record = csv.GetRecord<T>();
                        if (record != null)
                        {
                            batch.Add(record);
                            
                            if (batch.Count >= batchSize)
                            {
                                results.AddRange(batch);
                                batch.Clear();
                                batch.Capacity = batchSize;  // Maintenir la capacité pour éviter les réallocations
                            }
                        }
                    }
                    catch (CsvHelperException)
                    {
                        // Skip malformed records silently
                        continue;
                    }
                }

                // Add remaining records
                if (batch.Count > 0)
                {
                    results.AddRange(batch);
                }
            });

            return results;
        }
    }
}