#!/usr/bin/env node

var fs = require("fs");
var http = require("http");

var Meta = function()
{
};

Meta.prototype.load = function(path, callback)
{  
  var meta = this;
  
  meta.path = path;
  
  fs.readFile(meta.path, "utf8", function (err, data) {
    if (err) {
      meta.loadError = err;
    }
    else {
      try {
        meta.metaobj = eval("(function(){return " + data + ";})()");
      }
      catch (ex) {
        meta.loadError = ex + " in " + meta.path;
      }
    }
    if (meta.loadError) {
      console.log("Meta Load error: " + meta.loadError);
    }
    callback();
  });
};

var Server = function(path)
{
  var server = this;
  
  this.autoRefreshMeta = true;
  this.path = path;
  this.meta = null;
  
  this.httpServer = http.createServer(function(req, res) {
    if (server.autoRefreshMeta) {
      server.ensureMeta(function() {
        server.processRequest(req, res);
      });
    }
    else {
      server.processRequest(req, res);
    }
  });
};

Server.prototype.listen = function(port, hostname, callback)
{
  this.httpServer.listen(port, hostname, callback);
};

Server.prototype.close = function(callback)
{
  this.httpServer.on("close", callback);
  this.httpServer.close();
};

Server.prototype.ensureMeta = function(callback)
{
  if (!this.meta) {
    this.meta = new Meta(this.path);
    this.meta.load(this.path, callback);
  }
  else {
    callback();
  }
}

Server.prototype.processRequest = function(req, res)
{
  res.writeHead(200, {"Content-Type": "text/plain" });
  res.end("Yo");
};

exports.createServer = function(path)
{
  return new Server(path);
};
