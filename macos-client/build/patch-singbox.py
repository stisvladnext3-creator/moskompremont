"""Fix initial utun process detection without depending on an interface-cache event.

The kernel already identifies the source through the TUN inbound. Restricting
process lookup to a cached interface address can misroute the first app flow.
"""
from pathlib import Path

def apply(tree:Path):
 p=tree/'route/process_cache.go';text=p.read_text()
 old='if r.processSearcher == nil || metadata.ProcessInfo != nil || !r.isLocalSource(metadata.Source.Addr) {'
 new='if r.processSearcher == nil || metadata.ProcessInfo != nil || (!r.isLocalSource(metadata.Source.Addr) && metadata.InboundType != "tun") {'
 if text.count(old)!=1:raise RuntimeError('Pinned sing-box process-cache anchor changed')
 p.write_text(text.replace(old,new))
