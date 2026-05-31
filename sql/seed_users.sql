-- =============================================================================
-- OpsContext Users シードデータ
-- schema_users.sql を適用済みの DB に対して実行する。
-- パスワードはすべて "Demo@2026"。
-- 管理者は Role = 'Admin' で表現（IsAdmin 列は廃止）。
--
-- 注意: PasswordHash はデモ用のダミー値。
--       実際の PBKDF2 ハッシュは SqlUserStore.SeedIfEmptyAsync が自動生成するため、
--       本番環境では起動時の自動シードを使用すること。
-- =============================================================================

DELETE FROM Users;

-- 管理者アカウント
INSERT INTO Users (UserName, DisplayName, [Role], PasswordHash, IsActive)
VALUES ('user_system', N'システム管理者', 'Admin',
        'AAAAAAAAAAAAAAAAAAAAAIVZSQQtVFTQ0Iv+qr94T2Z7FHajZN+mp10hySWfwFAN', 1);

-- 業務アカウント
INSERT INTO Users (UserName, DisplayName, [Role], PasswordHash, IsActive)
VALUES ('user_sales', N'田中 一郎', 'Sales',
        'AAAAAAAAAAAAAAAAAAAAAIVZSQQtVFTQ0Iv+qr94T2Z7FHajZN+mp10hySWfwFAN', 1);

INSERT INTO Users (UserName, DisplayName, [Role], PasswordHash, IsActive)
VALUES ('user_purchasing', N'鈴木 花子', 'Purchasing',
        'AAAAAAAAAAAAAAAAAAAAAIVZSQQtVFTQ0Iv+qr94T2Z7FHajZN+mp10hySWfwFAN', 1);

INSERT INTO Users (UserName, DisplayName, [Role], PasswordHash, IsActive)
VALUES ('user_production', N'佐藤 次郎', 'Production',
        'AAAAAAAAAAAAAAAAAAAAAIVZSQQtVFTQ0Iv+qr94T2Z7FHajZN+mp10hySWfwFAN', 1);

INSERT INTO Users (UserName, DisplayName, [Role], PasswordHash, IsActive)
VALUES ('user_accounting', N'山田 三郎', 'Accounting',
        'AAAAAAAAAAAAAAAAAAAAAIVZSQQtVFTQ0Iv+qr94T2Z7FHajZN+mp10hySWfwFAN', 1);

SELECT UserId, UserName, DisplayName, [Role], IsActive FROM Users ORDER BY UserId;
