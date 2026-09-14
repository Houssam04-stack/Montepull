IF OBJECT_ID(N'dbo.mvp0_campaign_contexts',N'U') IS NULL
CREATE TABLE dbo.mvp0_campaign_contexts(
 campaign_id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
 cbn_run_id INT NOT NULL,
 pegging_run_id INT NOT NULL UNIQUE,
 payload_json NVARCHAR(MAX) NOT NULL,
 created_at DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
 CONSTRAINT FK_mvp0_context_campaign FOREIGN KEY(campaign_id) REFERENCES dbo.mvp0_campaigns(id),
 CONSTRAINT FK_mvp0_context_cbn FOREIGN KEY(cbn_run_id) REFERENCES dbo.cbn_runs(id),
 CONSTRAINT FK_mvp0_context_pegging FOREIGN KEY(pegging_run_id) REFERENCES dbo.pegging_runs(id)
);
