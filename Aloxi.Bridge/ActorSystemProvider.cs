﻿using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using Akka.Actor;
using Akka.Configuration;
using Akka.Logger.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ZoolWay.Aloxi.Bridge.Loxone;
using ZoolWay.Aloxi.Bridge.Meta;
using ZoolWay.Aloxi.Bridge.Mqtt;

namespace ZoolWay.Aloxi.Bridge
{
    public class ActorSystemProvider
    {
        private readonly IConfiguration configuration;
        private readonly ILogger<ActorSystemProvider> log;
        private ActorSystem actorSystem;
        private IActorRef mqttManager;
        private IActorRef metaProcessor;
        private IActorRef loxoneAdapter;
        private IActorRef alexaAdapter;
        private IActorRef statusConsolidator;

        public ActorSystem ActorSystem 
        { 
            get
            {
                if (this.actorSystem == null) throw new Exception("ActorSystem is not initialized!");
                return this.actorSystem;
            }
        }

        public IActorRef MqttManager
        {
            get => this.mqttManager;
        }

        public IActorRef LoxoneAdapter
        {
            get => this.loxoneAdapter;
        }

        public IActorRef StatusConsolidator
        {
            get => this.statusConsolidator;
        }

        public ActorSystemProvider(IConfiguration configuration, ILogger<ActorSystemProvider> log, ILoggerFactory loggerFactory)
        {
            this.configuration = configuration;
            this.log = log;
            this.actorSystem = null;
            this.mqttManager = ActorRefs.Nobody;
            this.metaProcessor = ActorRefs.Nobody;
            this.loxoneAdapter = ActorRefs.Nobody;
            this.alexaAdapter = ActorRefs.Nobody;
            this.statusConsolidator = ActorRefs.Nobody;

            LoggingLogger.LoggerFactory = loggerFactory;
        }

        public void Init()
        {
            log.LogInformation("Initializing ActorSystem");
            string configurationFile = Path.Join(AppContext.BaseDirectory, "akka.hocon");
            string configurationData = File.ReadAllText(configurationFile);
            Config akkaConfig = ConfigurationFactory.ParseString(configurationData);
            this.actorSystem = ActorSystem.Create("aloxi-bridge", akkaConfig);
            
            // build MQTT
            this.mqttManager = ActorRefs.Nobody;
            try
            {
                var c = configuration.GetSection("Mqtt");
                var mqttConfig = new Mqtt.MqttConfig(c["Endpoint"], c["CaPath"], c["CertPath"], c["ClientId"]);
                string subscriptionTopic = c["SubscriptionTopic"];
                string alexaResponseTopic = c["AlexaResponseTopic"];
                this.mqttManager = this.actorSystem.ActorOf(Props.Create(() => new Mqtt.ManagerActor(mqttConfig, subscriptionTopic, alexaResponseTopic)), "mqtt");
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Initializing of MQTT node failed");
            }

            // build meta
            this.metaProcessor = this.actorSystem.ActorOf(Props.Create(() => new Meta.MetaProcessorActor(this.mqttManager)), "meta");

            // build Loxone
            this.loxoneAdapter = ActorRefs.Nobody;
            try
            {
                var c = configuration.GetSection("Loxone");
                ImmutableArray<string> ignoreCats = c.GetSection("IgnoreCategories").GetChildren().ToList().Select(x => x.Value).ToImmutableArray();
                ImmutableArray<string> ignoreControls = c.GetSection("IgnoreControls").GetChildren().ToList().Select(x => x.Value).ToImmutableArray();
                var loxoneConfig = new Loxone.LoxoneConfig(c["Miniserver"], c["Username"], c["Password"], ignoreCats, ignoreControls);
                this.loxoneAdapter = this.actorSystem.ActorOf(Props.Create(() => new Loxone.AdapterActor(loxoneConfig)), "loxone");
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Initializing of Loxone node failed");
            }

            // build alexa
            this.alexaAdapter = ActorRefs.Nobody;
            try
            {
                this.alexaAdapter = this.actorSystem.ActorOf(Props.Create(() => new Alexa.AdapterActor(this.mqttManager, this.loxoneAdapter)),  "alexa");
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Initializing of Alexa node failed");
            }

            // init and configuration
            this.mqttManager.Tell(new MqttMessage.RegisterProcessor(Models.AloxiMessageOperation.Echo, this.metaProcessor));
            this.mqttManager.Tell(new MqttMessage.RegisterProcessor(Models.AloxiMessageOperation.PipeAlexaRequest, this.alexaAdapter));
            this.loxoneAdapter.Tell(new LoxoneMessage.InitAdapter());

            // status consolidator
            this.statusConsolidator = this.actorSystem.ActorOf(Props.Create(() => new StatusConsolidatorActor(this.loxoneAdapter, this.mqttManager)));
        }
    }
}
