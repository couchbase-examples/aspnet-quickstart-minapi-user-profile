﻿using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Newtonsoft.Json;
using Xunit;

namespace Couchbase.Quickstart.IntegrationTests;

public class UserProfileTests
    : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;
    private readonly string baseHostname = "/api/v1/profiles";
    private readonly string baseHostnameSearch = "/api/v1/profiles";

    public UserProfileTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
        _client = _factory.CreateClient();
    }

    [Fact]
    public async Task InsertProfileTestAsync()
    {
        //create user
        var userProfile = GetProfile();
        var newUser = JsonConvert.SerializeObject(userProfile);
        var content = new StringContent(newUser, Encoding.UTF8, "application/json");
        var response = await _client.PostAsync(baseHostname, content);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var jsonResults = await response.Content.ReadAsStringAsync();
        var newUserResult = JsonConvert.DeserializeObject<Profile>(jsonResults);

        //validate creation 
        Assert.Equal(userProfile.FirstName, newUserResult.FirstName);
        Assert.Equal(userProfile.LastName, newUserResult.LastName);
        Assert.Equal(userProfile.Email, newUserResult.Email);

        //remove user
        var deleteResponse = await _client.DeleteAsync($"{baseHostname}/{newUserResult.Pid}");
        Assert.Equal(HttpStatusCode.OK, deleteResponse.StatusCode);
    }

    [Fact]
    public async Task UpdateProfileTestAsync()
    {
        //create user
        var userProfile = GetProfile();
        var newUser = JsonConvert.SerializeObject(userProfile);
        var content = new StringContent(newUser, Encoding.UTF8, "application/json");
        var response = await _client.PostAsync(baseHostname, content);

        var jsonResults = await response.Content.ReadAsStringAsync();
        var newUserResult = JsonConvert.DeserializeObject<Profile>(jsonResults);

        //update user
        UpdateProfile(newUserResult);

        var updateUserJson = JsonConvert.SerializeObject(newUserResult);
        var updateContent = new StringContent(updateUserJson, Encoding.UTF8, "application/json");
        var updateResponse = await _client.PutAsync($"{baseHostname}/", updateContent);

        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        var jsonResult = await updateResponse.Content.ReadAsStringAsync();
        var updateUserResult = JsonConvert.DeserializeObject<Profile>(updateUserJson);

        //validate update worked
        Assert.Equal(newUserResult.Email, updateUserResult.Email);
        Assert.Equal(newUserResult.FirstName, updateUserResult.FirstName);
        Assert.Equal(newUserResult.LastName, updateUserResult.LastName);

        //remove user
        var deleteResponse = await _client.DeleteAsync($"{baseHostname}/{updateUserResult.Pid}");
        Assert.Equal(HttpStatusCode.OK, deleteResponse.StatusCode);

    }

    [Fact]
    public async Task GetProfileTestAsync()
    {
        //create user
        var userProfile = GetProfile();
        var newUser = JsonConvert.SerializeObject(userProfile);
        var content = new StringContent(newUser, Encoding.UTF8, "application/json");
        var response = await _client.PostAsync(baseHostname, content);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var jsonResults = await response.Content.ReadAsStringAsync();
        var newUserResult = JsonConvert.DeserializeObject<Profile>(jsonResults);

        //get the user from the main API
        var getResponse = await _client.GetAsync($"{baseHostname}/{newUserResult.Pid}");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        var getJsonResult = await getResponse.Content.ReadAsStringAsync();
        var getUserResult = JsonConvert.DeserializeObject<Profile>(getJsonResult);

        //validate it got the same user
        Assert.Equal(newUserResult.Email, getUserResult.Email);
        Assert.Equal(newUserResult.FirstName, getUserResult.FirstName);
        Assert.Equal(newUserResult.LastName, getUserResult.LastName);

        //remove user
        var deleteResponse = await _client.DeleteAsync($"{baseHostname}/{newUserResult.Pid}");
        Assert.Equal(HttpStatusCode.OK, deleteResponse.StatusCode);
    }

    [Fact]
    public async Task GetProfileSearchTestAsync()
    {
        await Task.Delay(10000);
        //create user
        var userProfile = GetProfile();
        var newUser = JsonConvert.SerializeObject(userProfile);
        var content = new StringContent(newUser, Encoding.UTF8, "application/json");
        var response = await _client.PostAsync(baseHostname, content);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var jsonResults = await response.Content.ReadAsStringAsync();
        var newUserResult = JsonConvert.DeserializeObject<Profile>(jsonResults);

        //get the user from the main API
        var getResponse = await _client.GetAsync($"{baseHostnameSearch}?search={userProfile.FirstName}&skip=0&limit=5");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        var getJsonResult = await getResponse.Content.ReadAsStringAsync();
        var getUserResult = JsonConvert.DeserializeObject<List<Profile>>(getJsonResult);

        //validate it got the same user
        Assert.Equal(newUserResult.Email, getUserResult[0].Email);
        Assert.Equal(newUserResult.FirstName, getUserResult[0].FirstName);
        Assert.Equal(newUserResult.LastName, getUserResult[0].LastName);

        //remove user
        var deleteResponse = await _client.DeleteAsync($"{baseHostname}/{newUserResult.Pid}");
        Assert.Equal(HttpStatusCode.OK, deleteResponse.StatusCode);
    }

    private Profile GetProfile()
    {
        return new ProfileCreateRequestCommand()
        {
            FirstName = "John",
            LastName = "Doe",
            Email = "john.doe@couchbase.com",
            Password = "password"
        }.GetProfile();
    }

    private void UpdateProfile(Profile profile)
    {
        profile.FirstName = "Jane";
        profile.LastName = "Smith";
        profile.Email = "Jane.Smith@couchbase.com";
        profile.Password = "password1";
    }
}
