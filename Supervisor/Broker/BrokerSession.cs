using System;using System.Collections.Generic;
namespace SpatialWallpaper.Supervision.Broker {
 // Created only by trusted launch/admission code. Never deserialize this class from a Worker message.
 public sealed class BrokerSession {
  readonly object gate=new object();readonly HashSet<string> capabilities;bool closed;
  public Guid Installation{get;}public Guid Attempt{get;}public Guid Instance{get;}public uint Generation{get;}public bool Preview{get;}
  public BrokerSession(AttemptReceipt receipt,bool preview,IEnumerable<string> admittedCapabilities){
   if(receipt==null||admittedCapabilities==null)throw new ArgumentNullException();
   Installation=receipt.Installation;Attempt=receipt.Attempt;Instance=receipt.Instance;Generation=receipt.Generation;Preview=preview;
   capabilities=new HashSet<string>(admittedCapabilities,StringComparer.Ordinal);if(capabilities.Count>32)throw new ArgumentException("capability-count");
  }
  public void Demand(string capability){lock(gate){if(closed)throw new InvalidOperationException("broker-session-closed");if(!capabilities.Contains(capability))throw new UnauthorizedAccessException("capability-not-admitted");}}
  public bool Allows(string capability){lock(gate)return !closed&&capabilities.Contains(capability);}
  // Linearization point: accepted atomic commits may finish after Close. Close never waits for disk I/O.
  internal void BeginCommit(){Demand("private-storage");}
  public void Close(){lock(gate)closed=true;}
 }
}
