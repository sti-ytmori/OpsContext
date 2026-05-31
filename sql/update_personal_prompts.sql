-- =============================================================================
-- ユーザープロフィール 手動設定スクリプト
-- 各ユーザーが「どんな人物か」をエージェントに伝える情報です。
-- 内容を好みに合わせて編集してから実行してください。
-- =============================================================================

-- 営業担当: 田中 一郎
UPDATE Users SET PersonalPrompt = N'関西エリア担当の営業。大手製造業への新規開拓が得意で、与信交渉の経験が豊富。数字より顧客との関係性を重視する傾向があり、分割受注や代替提案を好む。月末に案件を集中させがちなため、納期タイトな案件の相談が多い。'
WHERE UserName = 'user_sales';

-- 購買担当: 鈴木 花子
UPDATE Users SET PersonalPrompt = N'購買歴10年のベテラン担当。複数ベンダーの比較交渉を得意とし、リードタイム短縮の実績多数。安全在庫の確保を最優先に考え、緊急調達よりも計画調達を強く好む。コスト削減提案には積極的に反応する。'
WHERE UserName = 'user_purchasing';

-- 生産管理担当: 佐藤 次郎
UPDATE Users SET PersonalPrompt = N'生産スケジュール管理の責任者。工程ごとのボトルネックを素早く見抜く経験を持つ。優先生産の調整権限を持っているが、ラインへの影響を非常に気にするため、変更提案には必ず影響試算を求める。分割納品での対応を現実的な選択肢として評価する。'
WHERE UserName = 'user_production';

-- 経理担当: 山田 三郎
UPDATE Users SET PersonalPrompt = N'経理部門のリーダー。与信管理と月次決算の締めを最優先事項として動いている。1,000万円超の受注は必ず経理承認フローを通す運用を徹底している。支払遅延歴のある顧客には保守的な判断を下す傾向があり、数値の根拠を明確に示すことを好む。'
WHERE UserName = 'user_accounting';

-- 管理者: システム管理者
UPDATE Users SET PersonalPrompt = N'システム全体の管理者。各部門の業務フローを横断的に把握しており、部門間の調整役も担う。設定変更やアカウント管理の問い合わせ対応も行う。'
WHERE UserName = 'user_system';

-- 確認
SELECT UserName, DisplayName, Role, LEFT(PersonalPrompt, 50) AS PromptPreview
FROM Users
WHERE IsActive = 1
ORDER BY UserId;
