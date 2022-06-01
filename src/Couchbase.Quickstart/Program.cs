using System.Reflection;
using Couchbase.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

/// <summary>
/// dev origins used to fix CORS for local dev/qa debugging of site
/// </summary>
const string _devSpecificOriginsName = "_devAllowSpecificOrigins";

// Add services to the container.
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

//register the configuration for Couchbase and Dependency Injection Framework
builder.Services.Configure<CouchbaseConfig>(builder.Configuration.GetSection("Couchbase"));
builder.Services.AddCouchbase(builder.Configuration.GetSection("Couchbase"));
builder.Services.AddHttpClient();

//add Database Service
builder.Services.AddTransient<Couchbase.Quickstart.Services.DatabaseService>();

//fix for debugging dev and qa environments in GitPod
//DO NOT APPLY to UAT or Production Environments!!!
builder.Services.AddCors(options =>
{
    options.AddPolicy(name: _devSpecificOriginsName,
        builder =>
        {
            builder.WithOrigins("https://*.gitpod.io",
                                "https://*.github.com",
                                "http://localhost:5000",
                                "https://localhost:5001")
                                .AllowAnyHeader()
                                .AllowAnyMethod()
                                .AllowCredentials();
        });
});

// get app reference
var app = builder.Build();

//remove couchbase from memory when ASP.NET closes
app.Lifetime.ApplicationStopped.Register(() =>
{
    var cls = app.Services.GetRequiredService<ICouchbaseLifetimeService>();
    if (cls != null)
    {
        cls.Close();
    }
});

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
if (app.Environment.EnvironmentName == "Testing")
{
    app.UseCors(_devSpecificOriginsName);
    //assume that bucket, collection, and indexes already exists due to latency in creating and async 
}
else
{
    //setup the database once everything is setup and running
    app.Lifetime.ApplicationStarted.Register(async () =>
    {
        var db = app.Services.GetService<Couchbase.Quickstart.Services.DatabaseService>();

        //**WARNING** - this code assumes the bucket has already been created
        //if you don't create it you will get errors
        if (db != null)
        {
            //create collection to store documents in
            await db.CreateCollection();

            //creates the indexes for our SQL++ query
            await db.CreateIndex();
        }
    });
}

app.UseHttpsRedirection();

//API Routes
app.MapGet("/api/v1/profiles", async (string? search, int? limit, int? skip, IClusterProvider clusterProvider, IOptions<CouchbaseConfig> options) =>
{
    try
    {
        if (search != null)
        {
            //get couchbase config values from appsettings.json 
            var couchbaseConfig = options.Value;

            //create query using parameters to advoid SQL Injection
            var cluster = await clusterProvider.GetClusterAsync();
            var query = $@"SELECT p.* FROM `{couchbaseConfig.BucketName}`.`{couchbaseConfig.ScopeName}`.`{couchbaseConfig.CollectionName}` p WHERE lower(p.firstName) LIKE '%' || $search || '%' OR lower(p.lastName) LIKE '%' || $search || '%' LIMIT $limit OFFSET $skip";

            //setup parameters
            var queryParameters = new Couchbase.Query.QueryOptions();
            queryParameters.Parameter("search", search.ToLower());
            queryParameters.Parameter("limit", limit == null ? 5 : limit);
            queryParameters.Parameter("skip", skip == null ? 0 : skip);

            var results = await cluster.QueryAsync<Profile>(query, queryParameters);

            var items = await results.Rows.ToListAsync<Profile>();
            if (items.Count() == 0)
                return Results.NotFound();

            return Results.Ok(items);
        }
        else
        {
            return Results.BadRequest();
        }

    }
    catch (Exception ex)
    {
        return Results.Problem(statusCode: StatusCodes.Status500InternalServerError, detail: $"Error: {ex.Message} {ex.StackTrace}");
    }
});

app.MapGet("/api/v1/profiles/{id}", async (Guid id, IBucketProvider bucketProvider, IOptions<CouchbaseConfig> options) =>
{
    try
    {
        //get couchbase config values from appsettings.json 
        var couchbaseConfig = options.Value;

        //get the bucket, scope, and collection
        var bucket = await bucketProvider.GetBucketAsync(couchbaseConfig.BucketName);
        var scope = bucket.Scope(couchbaseConfig.ScopeName);
        var collection = scope.Collection(couchbaseConfig.CollectionName);

        //get the docment from the bucket using the id
        var result = await collection.GetAsync(id.ToString());

        //validate we have a document
        var resultProfile = result.ContentAs<Profile>();
        if (resultProfile != null)
        {
            return Results.Ok(resultProfile);
        }
    }
    catch (Couchbase.Core.Exceptions.KeyValue.DocumentNotFoundException)
    {
        Results.NotFound();
    }
    catch (Exception ex)
    {
        return Results.Problem(ex.Message);
    }

    return Results.NotFound();

});

app.MapPost("/api/v1/profiles", async (ProfileCreateRequestCommand request, IBucketProvider bucketProvider, IOptions<CouchbaseConfig> options) =>
{
    //get couchbase config values from appsettings.json 
    var couchbaseConfig = options.Value;

    //get the bucket, scope, and collection
    var bucket = await bucketProvider.GetBucketAsync(couchbaseConfig.BucketName);
    var scope = bucket.Scope(couchbaseConfig.ScopeName);
    var collection = scope.Collection(couchbaseConfig.CollectionName);

    //get profile from request
    var profile = request.GetProfile();

    //set documentId 
    profile.Pid = Guid.NewGuid();

    //save documentg
    await collection.InsertAsync(profile.Pid.ToString(), profile);
    return Results.Created($"/api/v1/profile/{profile.Pid}", profile);
});

app.MapPut("/api/v1/profiles", async (ProfileUpdateRequestCommand request, IBucketProvider bucketProvider, IOptions<CouchbaseConfig> options) =>
{
    //get couchbase config values from appsettings.json 
    var couchbaseConfig = options.Value;

    //get the bucket, scope, and collection
    var bucket = await bucketProvider.GetBucketAsync(couchbaseConfig.BucketName);
    var collection = bucket.Collection(couchbaseConfig.CollectionName);

    //get current profile from the database
    var result = await collection.GetAsync(request.Pid.ToString());
    if (result != null)
    {
        var profile = result.ContentAs<Profile>();
        var updateResult = await collection.ReplaceAsync<Profile>(request.Pid.ToString(), request.GetProfile());

        return Results.Ok(request);
    }
    else
    {
        return Results.NotFound();
    }
});

app.MapDelete("/api/v1/profiles/{id}", async(Guid id, IBucketProvider bucketProvider, IOptions < CouchbaseConfig > options) => 
{

    //get couchbase config values from appsettings.json 
    var couchbaseConfig = options.Value;

    //get the bucket and collection
    var bucket = await bucketProvider.GetBucketAsync(couchbaseConfig.BucketName);
    var collection = bucket.Collection(couchbaseConfig.CollectionName);

    //get the docment from the bucket using the id
    var result = await collection.GetAsync(id.ToString());

    //validate we have a document
    var resultProfile = result.ContentAs<Profile>();
    if (resultProfile != null)
    {
        await collection.RemoveAsync(id.ToString());
        return Results.Ok(id);
    }
    else
    {
        return Results.NotFound();
    }
});

app.Run();

// required for integration testing from asp.net
// https://docs.microsoft.com/en-us/aspnet/core/test/integration-tests?view=aspnetcore-6.0
public partial class Program { }

public record Profile
{
    public Guid Pid { get; set; }
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string Email { get; set; } = "";

    private string _password = "";
    public string Password
    {
        get
        {
            return _password;
        }
        set
        {
            _password = BCrypt.Net.BCrypt.HashPassword(value);
        }
    }
}

public record CouchbaseConfig
{
    public string BucketName { get; set; } = "";
    public string CollectionName { get; set; } = "";
    public string ScopeName { get; set; } = "";
    public string RestEndpoint { get; set; } = "";

    public bool IgnoreRemoteCertificateNameMismatch { get; set; }
    public bool HttpIgnoreRemoteCertificateMismatch { get; set; }
    public bool KvIgnoreRemoteCertificateNameMismatch { get; set; }

    public string ConnectionString { get; set; } = "";
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
}


public record ProfileCreateRequestCommand
{
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string Email { get; set; } = "";
    public string Password { get; set; } = "";

    public Profile GetProfile()
    {
        return new Profile
        {
            Pid = new Guid(),
            FirstName = this.FirstName,
            LastName = this.LastName,
            Email = this.Email,
            Password = this.Password
        };
    }
}

public record ProfileUpdateRequestCommand
{
    public Guid Pid { get; set; }
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string Email { get; set; } = "";
    public string Password { get; set; } = "";

    public Profile GetProfile()
    {
        return new Profile
        {
            Pid = this.Pid,
            FirstName = this.FirstName,
            LastName = this.LastName,
            Email = this.Email,
            Password = this.Password
        };
    }
}