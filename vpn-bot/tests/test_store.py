import dataclasses
import time
from concurrent.futures import ThreadPoolExecutor

from vpn_bot.store import Store
from conftest import add_reserve, allow, report, sample


def test_two_failures_alert_once_and_two_successes_recover(store, cfg):
    report(store, cfg)
    report(store, cfg, results=[sample(ok=False)])
    assert store.rows("SELECT state FROM checks WHERE probe_id='ru' AND server_id='one'")[0]["state"] == "up"
    assert not store.healthy_servers(cfg)
    report(store, cfg, results=[sample(ok=False)])
    report(store, cfg, results=[sample(ok=False)])
    assert len(store.rows("SELECT * FROM outbox")) == 1
    report(store, cfg)
    assert not store.healthy_servers(cfg)
    report(store, cfg)
    assert store.healthy_servers(cfg) == {"one"}
    assert len(store.rows("SELECT * FROM outbox")) == 2


def test_retry_batch_does_not_count_as_another_failure(store, cfg):
    batch = "same-report"
    assert report(store, cfg, results=[sample(ok=False)], batch=batch)
    assert not report(store, cfg, results=[sample(ok=False)], batch=batch)
    row = store.rows("SELECT * FROM checks WHERE probe_id='ru' AND server_id='one'")[0]
    assert row["failures"] == 1 and row["state"] == "unknown"


def test_tcp_outside_and_stale_results_never_unlock_links(store, cfg):
    report(store, cfg, probe="control")
    assert not store.healthy_servers(cfg)
    report(store, cfg, results=[sample(kind="tcp")])
    assert not store.healthy_servers(cfg)
    report(store, cfg, now=time.time() - cfg.stale_after - 1)
    assert not store.healthy_servers(cfg)


def test_quorum_requires_distinct_russian_probes(store, cfg):
    from vpn_bot.config import Probe
    cfg = dataclasses.replace(cfg, min_ru_successes=2, probes=cfg.probes + (Probe("ru2", "РФ 2", "ru", "RU2_TOKEN"),))
    store.init_checks(cfg)
    report(store, cfg)
    report(store, cfg)
    assert not store.healthy_servers(cfg)
    report(store, cfg, probe="ru2")
    assert store.healthy_servers(cfg) == {"one"}


def test_missing_probe_alerts_once_and_requires_recovery(store, cfg):
    old = time.time() - 500
    store.execute("UPDATE checks SET created=?", (old,))
    report(store, cfg, now=old)
    store.mark_stale(cfg, (999,))
    count = len(store.rows("SELECT * FROM outbox"))
    store.mark_stale(cfg, (999,))
    assert len(store.rows("SELECT * FROM outbox")) == count == 2
    report(store, cfg)
    assert not store.healthy_servers(cfg)
    report(store, cfg)
    assert store.healthy_servers(cfg) == {"one"}


def test_only_approved_customer_gets_personal_link_and_reuses_it(store, cfg):
    store.register(1, "pending")
    add_reserve(store, count=2)
    report(store, cfg)
    assert store.issue(1, cfg)[1] == "not_allowed"
    allow(store, 1)
    first, reason = store.issue(1, cfg)
    assert reason == "issued"
    second, reason = store.issue(1, cfg)
    assert reason == "existing" and second["id"] == first["id"]
    allow(store, 2)
    third, _ = store.issue(2, cfg)
    assert third["id"] != first["id"]


def test_failure_prefers_another_working_host(store, cfg):
    allow(store, 1)
    add_reserve(store, "one", 2)
    add_reserve(store, "two", 1)
    report(store, cfg, results=[sample("one"), sample("two")])
    first, _ = store.issue(1, cfg)
    assert first["server_id"] == "one"
    report(store, cfg, results=[sample("one", ok=False), sample("two")])
    second, _ = store.issue(1, cfg)
    assert second["server_id"] == "two"


def test_parallel_allocations_across_connections_never_share_a_link(tmp_path, cfg):
    path = str(tmp_path / "bot.db")
    primary = Store(path)
    primary.init_checks(cfg)
    for uid in range(1, 21):
        allow(primary, uid)
    add_reserve(primary, count=15)
    report(primary, cfg)

    def issue(uid):
        other = Store(path)
        try:
            result, _ = other.issue(uid, cfg)
            return result["id"] if result else None
        finally:
            other.close()

    with ThreadPoolExecutor(max_workers=8) as pool:
        allocated = [value for value in pool.map(issue, range(1, 21)) if value is not None]
    assert len(allocated) == len(set(allocated)) == 15
    assert primary.rows("SELECT COUNT(*) AS n FROM links WHERE assigned_to IS NULL")[0]["n"] == 0
    primary.close()


def test_rotating_profile_invalidates_previous_health(store, cfg):
    store.save_profile("one", {"outbound": {"protocol": "vless", "key": "old"}})
    report(store, cfg)
    assert store.healthy_servers(cfg)
    store.save_profile("one", {"outbound": {"protocol": "vless", "key": "new"}})
    assert not store.healthy_servers(cfg)


def test_disabled_reserve_is_not_issued(store, cfg):
    allow(store, 1)
    add_reserve(store)
    report(store, cfg)
    store.execute("UPDATE links SET enabled=0")
    assert store.issue(1, cfg)[1] == "no_verified_reserve"


def test_slow_latency_fluctuations_do_not_spam_owner(store, cfg):
    report(store, cfg)
    report(store, cfg, results=[sample(latency=cfg.slow_ms + 10)])
    report(store, cfg, results=[sample(latency=40)])
    assert not store.rows("SELECT * FROM outbox")
    report(store, cfg, results=[sample(latency=cfg.slow_ms + 10)])
    report(store, cfg, results=[sample(latency=cfg.slow_ms + 10)])
    assert len(store.rows("SELECT * FROM outbox")) == 1


def test_removed_server_cannot_supply_reserves_after_config_change(store, cfg):
    allow(store, 1)
    add_reserve(store, "two")
    report(store, cfg, results=[sample("two")])
    reduced = dataclasses.replace(cfg, servers=cfg.servers[:1])
    assert not store.healthy_servers(reduced)
    assert store.issue(1, reduced)[1] == "no_verified_reserve"
    store.init_checks(reduced)
    assert not store.rows("SELECT * FROM checks WHERE server_id='two'")
