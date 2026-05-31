# タスク: Homeページ改善 + デモ導線整備

Status: 未着手

## 目的

審査員がアプリを開いた最初の5秒で価値が伝わること、かつデモシナリオ（ロール切替による視点変化）を案内なしで再現できること。

Casesページはなくなったため、デモ導線は「固定caseIdのチャットに直接飛ばす」方式で実現する。

---

## 変更対象ファイル

- `OpsContext.Web/Components/Pages/Home.razor`
- `OpsContext.Web/Components/Layout/MainLayout.razor`

---

## タスク一覧

### Home.razor

- [ ] ヒーローセクションのサブテキストを機能説明から価値訴求に変更する（下記「変更仕様」参照）
- [ ] クイックアクションカードを3枚 → 4枚に増やし「デモを試す」カードを追加する
  - アイコン: `Icons.Material.Outlined.PlayArrow`
  - 背景色: `#ede7f6`（パープル系）/ アイコン色: `#6a1b9a`
  - 遷移先: `/chat?caseId=a1b2c3d4-0001-0000-0000-000000000001`
  - キャプション例:
    ```
    A商事 受注案件を開き、
    Sales → Accounting のロール切替で
    視点の違いを確認する
    ```
- [ ] カード下部にデモフロー説明を追記する（下記「デモフロー説明」参照）

### MainLayout.razor

- [ ] ヘッダーナビに「Admin」リンクを追加する（既存ナビリンクの末尾に、目立たないサイズで配置）
  - `Href="/admin"` / アイコン: `Icons.Material.Outlined.AdminPanelSettings`
  - ラベル: "Admin"

---

## 変更仕様

### ヒーローセクション サブテキスト（Home.razor）

現状:
```
ERP データを Grid で管理し、AI エージェントと協働して業務判断をコンテキストに記録するプラットフォームです。
```

変更後（目安):
```
同じ案件データを、営業は「受注チャンス」として見て、
経理は「回収リスク」として読む。
OpsContext は、ロールごとにエージェントの語り口を切り替え、
引継ぎのたびに失われていた文脈を自動で継承します。
```

表示幅の都合で折り返し位置は調整可。

### デモフロー説明（Home.razor — カード群の直下に追記）

```razor
<MudPaper Elevation="0"
          Style="background:#f0f4ff; border:1px solid #c8d8ff; border-radius:12px; padding:20px 24px; margin-top:8px; max-width:640px; margin-left:auto; margin-right:auto;">
    <MudText Typo="Typo.caption" Style="font-weight:700; color:#1565c0; text-transform:uppercase; letter-spacing:0.5px; margin-bottom:10px; display:block;">
        デモシナリオの流れ
    </MudText>
    <div style="display:flex; flex-direction:column; gap:6px;">
        <MudText Typo="Typo.body2" Style="color:#1a1a2e;">
            1. 右上のロール選択で「Sales（営業）」を選ぶ
        </MudText>
        <MudText Typo="Typo.body2" Style="color:#1a1a2e;">
            2. 「デモを試す」でA商事案件を開き、エージェントに質問する
        </MudText>
        <MudText Typo="Typo.body2" Style="color:#1a1a2e;">
            3. 「この線で進めます」を押して判断を記録する
        </MudText>
        <MudText Typo="Typo.body2" Style="color:#1a1a2e;">
            4. ロールを「Accounting（経理）」に切り替えて同じ案件を開く
        </MudText>
        <MudText Typo="Typo.body2" Style="color:#1a1a2e;">
            5. 営業視点の回答と経理視点の回答を比較する
        </MudText>
    </div>
</MudPaper>
```

---

## 補足: 固定デモcaseIdについて

モックモードのSeedSampleDataが投入するA商事案件のcaseId:

```
a1b2c3d4-0001-0000-0000-000000000001
```

- タイトル: A商事 弁P-101 150個受注検討（進行中）
- 既に営業・生産・経理の全ロールのコンテキストエントリが入っている
- このIDに直接アクセスすれば、ロール切替の前後対比がすぐに確認できる

本番モード（Azure SQL）の場合はseed.sqlにも同じcaseIdを固定で INSERT する必要がある。
現時点では `--mock` フラグでの動作を主デモと割り切ることで問題ない。

---

## 受け入れ条件

- [ ] トップページを開いた審査員が、スクロールせずに「ロール別翻訳エージェント」の価値を読み取れる
- [ ] 「デモを試す」を押すとA商事案件のチャット画面が開く
- [ ] デモフローの説明を読めば、案内なしにロール切替のデモを再現できる
- [ ] Adminページへのリンクがヘッダーから辿れる
