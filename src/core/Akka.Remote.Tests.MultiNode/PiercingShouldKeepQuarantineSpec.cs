﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Remote.TestKit;

namespace Akka.Remote.Tests.MultiNode
{
    public class PiercingShouldKeepQuarantineMultiNodeConfig : MultiNodeConfig
    {
        public RoleName First { get; private set; }
        public RoleName Second { get; private set; }

        public PiercingShouldKeepQuarantineMultiNodeConfig()
        {
            First = Role("first");
            Second = Role("second");

            CommonConfig = DebugConfig(true)
                .WithFallback(ConfigurationFactory.ParseString(@"
    akka.loglevel = INFO
    akka.remote.log-remote-lifecycle-events = INFO
    akka.remote.retry-gate-closed-for = 10s
                "));
        }
    }

    public class PiercingShouldKeepQuarantineMultiNode1 : PiercingShouldKeepQuarantineSpec
    {
    }

    public class PiercingShouldKeepQuarantineMultiNode2 : PiercingShouldKeepQuarantineSpec
    {
    }

    public abstract class PiercingShouldKeepQuarantineSpec : MultiNodeSpec
    {
        private readonly PiercingShouldKeepQuarantineMultiNodeConfig _config;

        protected PiercingShouldKeepQuarantineSpec() : this(new PiercingShouldKeepQuarantineMultiNodeConfig())
        {
        }

        protected PiercingShouldKeepQuarantineSpec(PiercingShouldKeepQuarantineMultiNodeConfig config) : base(config)
        {
            _config = config;
        }

        protected override int InitialParticipantsValueFactory
        {
            get { return Roles.Count; }
        }

        public class Subject : UntypedActor
        {
            protected override void OnReceive(object message)
            {
                if (message.Equals("getuid"))
                    Sender.Tell(AddressUidExtension.Uid(Context.System));
            }
        }

        [MultiNodeFact]
        public void PiercingShouldKeepQuarantineSpecs()
        {
            WhileProbingThroughTheQuarantineRemotingMustNotLoseExistingQuarantineMarker();
        }

        public void WhileProbingThroughTheQuarantineRemotingMustNotLoseExistingQuarantineMarker()
        {
            RunOn(() =>
            {
                EnterBarrier("actors-started");

                // Communicate with second system
                Sys.ActorSelection(Node(_config.Second) / "user" / "subject").Tell("getuid");
                var uid = ExpectMsg<int>(TimeSpan.FromSeconds(10));
                EnterBarrier("actor-identified");

                // Manually Quarantine the other system
                RARP.For(Sys).Provider.Transport.Quarantine(Node(_config.Second).Address, uid);

                // Quarantining is not immedeiate
                Thread.Sleep(1000);

                // Quarantine is up - Should not be able to communicate with remote system any more
                for (var i = 1; i <= 4; i++)
                {
                    Sys.ActorSelection(Node(_config.Second) / "user" / "subject").Tell("getuid");

                    ExpectNoMsg(TimeSpan.FromSeconds(2));
                }

                EnterBarrier("quarantine-intact");

            }, _config.First);

            RunOn(() =>
            {
                Sys.ActorOf<Subject>("subject");
                EnterBarrier("actors-started");
                EnterBarrier("actor-identified");
                EnterBarrier("quarantine-intact");
            }, _config.Second);
        }
    }
}
