#!/usr/bin/env node

var http = require("http");

var Server = function ()
{
  this.httpServer = http.createServer(function(req, resp) {
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

exports.createServer = function(path)
{
  return new Server();
};
