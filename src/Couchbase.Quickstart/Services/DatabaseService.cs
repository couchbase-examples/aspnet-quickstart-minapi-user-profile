﻿using System;

using Couchbase;
using Couchbase.Core.Exceptions;
using Couchbase.Extensions.DependencyInjection;
using Couchbase.Management.Buckets;
using Couchbase.Management.Collections;
using Couchbase.Query;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Couchbase.Quickstart.Services
{
	public class DatabaseService
	{
		private readonly IClusterProvider _clusterProvider;
		private readonly IBucketProvider _bucketProvider;
		private readonly ILogger<DatabaseService> _logger;
		private readonly CouchbaseConfig _couchbaseConfig;

		public DatabaseService(
			IClusterProvider clusterProvider,
			IBucketProvider bucketProvider,
			IOptions<CouchbaseConfig> options,
			ILogger<DatabaseService> logger)
		{
			_clusterProvider = clusterProvider;
			_bucketProvider = bucketProvider;
			_couchbaseConfig = options.Value;
			_logger = logger;

		}

		public async Task CreateIndex()
		{
			ICluster? cluster = null;
			//try to create index - if fails it probably already exists
			try
			{
				_logger.LogInformation("**INFO** Trying to create Indexes...");
				cluster = await _clusterProvider.GetClusterAsync().ConfigureAwait(false);
				_logger.LogInformation("**INFO** Create Indexes - Cluster returned.");
				if (cluster != null)
				{
					var queries = new List<string>
					{
						$"CREATE PRIMARY INDEX default_profile_index ON {_couchbaseConfig.BucketName}.{_couchbaseConfig.ScopeName}.{_couchbaseConfig.CollectionName}",
						$"CREATE Primary INDEX on {_couchbaseConfig.BucketName}"
					};
					foreach (var query in queries)
					{
						_logger.LogInformation("**INFO** Running Create Index query: {query}", query);
						var result = await cluster.QueryAsync<dynamic>(query).ConfigureAwait(false);
						if (result.MetaData != null && result.MetaData.Status != QueryStatus.Success)
						{
							_logger.LogError("**ERROR** Couldn't create index required with {Query}", query);
							throw new System.Exception($"Error create index didn't return proper results for index {query}");
						}
					}
				}
				else
				{
					_logger.LogError("**ERROR** Couldn't create indexes, cluster is null");
				}
			}
			catch (IndexExistsException)
			{
				_logger.LogWarning($"Collection {_couchbaseConfig.CollectionName} already exists in {_couchbaseConfig.BucketName}.");
			}
			catch (System.Exception ex)
			{
				_logger.LogError("**ERROR** {Message}", ex.Message);
			}
		}
		public async Task CreateCollection()
		{
			IBucket? bucket = null;

			try
			{
				_logger.LogInformation("**INFO** Opening Bucket <{BucketName}> with username: <{Username}> and password:<{Password}>",
				_couchbaseConfig.BucketName,
				_couchbaseConfig.Username,
				_couchbaseConfig.Password);
				bucket = await _bucketProvider.GetBucketAsync(_couchbaseConfig.BucketName);
			}
			catch (System.Exception ex)
			{
				_logger.LogError("**ERROR**  Couldn't connect to bucket {BucketName} with username: <{Username}> and password: <{Password}> with Error: {Error}",
					_couchbaseConfig.BucketName,
					_couchbaseConfig.Username,
					_couchbaseConfig.Password,
					ex.Message);

			}
			if (bucket != null)
			{
				if (!_couchbaseConfig.ScopeName.StartsWith("_"))
				{
					//try to create scope - if fails it's ok we are probably using default
					try
					{
						await bucket.Collections.CreateScopeAsync(_couchbaseConfig.CollectionName);
					}
					catch (ScopeExistsException)
					{
						_logger.LogWarning($"Scope {_couchbaseConfig.ScopeName} already exists, probably default");
					}
					catch (HttpRequestException)
					{
						_logger.LogWarning($"HttpRequestExcecption when creating Scope {_couchbaseConfig.ScopeName}");
					}
				}

				//try to create collection - if fails it's ok the collection probably exists
				try
				{
					_logger.LogInformation("**INFO** Creating Collection:  <{CollectionName}> with username: <{Username}> and password:<{Password}> in bucket <{BucketName}>",
						_couchbaseConfig.CollectionName,
						_couchbaseConfig.Username,
						_couchbaseConfig.Password,
						_couchbaseConfig.BucketName);
					await bucket.Collections.CreateCollectionAsync(new CollectionSpec(_couchbaseConfig.ScopeName, _couchbaseConfig.CollectionName));
				}
				catch (CollectionExistsException)
				{
					_logger.LogWarning($"Collection {_couchbaseConfig.CollectionName} already exists in {_couchbaseConfig.BucketName}.");
				}
				catch (HttpRequestException)
				{
					_logger.LogWarning($"HttpRequestExcecption when creating collection  {_couchbaseConfig.CollectionName}");
				}
			}
			else
				throw new System.Exception("Can't retreive bucket.");
		}

		public async Task CreateBucket()
		{
			ICluster? cluster = null;

			//try to create bucket, if exists will just fail which is fine
			try
			{
				cluster = await _clusterProvider.GetClusterAsync();
				if (cluster != null)
				{
					var bucketSettings = new BucketSettings
					{
						Name = _couchbaseConfig.BucketName,
						BucketType = BucketType.Couchbase,
						RamQuotaMB = 256,

					};

					await cluster.Buckets.CreateBucketAsync(bucketSettings);
				}
				else
					throw new System.Exception("Can't create bucket - cluster is null, please check database configuration.");
			}

			catch (BucketExistsException)
			{
				_logger.LogWarning($"Bucket {_couchbaseConfig.BucketName} already exists");
			}
			catch (CouchbaseException ce)
			{
				_logger.LogError(ce.Message);
			}
			catch (System.Exception ex)
			{
				_logger.LogError(ex.Message);
			}
		}
	}
}

