#!/usr/bin/env node

var rouse = require("../lib/rouse.js");

var options = {
  test: false,
  port: 7331,
  hostname: "127.0.0.1"
};

var files = [];

function processCommandLine ()
{
  var i;
  var argv = process.argv;
  
  for (i = 2; i < argv.length; i++) {
    if (argv[i] == "--test") {
      options.test = true;
    }
    else {
      files.push(argv[i]);
    }
  }
}

function startServer(path, callback)
{
  var server = new rouse.createServer(path);
  server.listen(options.port, options.hostname, function() {
    console.log("Running " + path + " at http://" + options.address + ":" + options.port + "/");
    callback(server);
  });
}

function testServer(server, callback)
{
  var results = { passed: [], failed: [] };
  console.log("Testing");
  callback(results);
}

function stopServer(server, callback)
{
  server.close(function() {
    console.log("Stopping");
    callback();
  });
}

function run()
{
  var i = 0;
  var accTestResults = { passed: [], failed: [] };
  
  function runOne()
  {
    var file;
    
    if (i >= files.length) return;
    file = files[i];
    i++;
    
    startServer(file, function(server) {
      if (options.test) {
        testServer(server, function (testResults) {
          accTestResults.passed.concat(testResults.passed);
          accTestResults.failed.concat(testResults.failed);
          stopServer(server, function() {
            runOne();
          });
        });      
      }
      else {
        runOne();
      }      
    });
  }
  
  runOne();
}

processCommandLine();
run();
