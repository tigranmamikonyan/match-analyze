WITH CalculatedBets AS (
    SELECT
        m."MatchId",
        p."Date",
        p."HomeTeam",
        p."AwayTeam",
        p."AI_Over25_Prob" AS "ModelProbPct",
        p."AI_Over25_Prob" / 100.0 AS "ModelProb",
        m."GoalsCount",
        m."Over25Odds",
        1.0 / m."Over25Odds" AS "ImpliedProb",

        CASE
            WHEN m."GoalsCount" > 2 THEN 1
            ELSE 0
            END AS "ActualResult",

        CASE
            WHEN m."GoalsCount" > 2 THEN 'WIN ✅'
            ELSE 'LOSS ❌'
            END AS "Result",

        -- EV на 1 unit ставки
        (p."AI_Over25_Prob" / 100.0 * m."Over25Odds") - 1 AS "EV",

        -- Kelly fraction
        CASE
            WHEN m."Over25Odds" > 1
                THEN ((p."AI_Over25_Prob" / 100.0 * m."Over25Odds") - 1) / (m."Over25Odds" - 1)
            ELSE NULL
            END AS "Kelly",

        -- Сигнал ставить / не ставить
        CASE
            WHEN ((p."AI_Over25_Prob" / 100.0 * m."Over25Odds") - 1) > 0
                THEN 1
            ELSE 0
            END AS "BetFlag",

        -- Прибыль на 1 unit ставки
        CASE
            WHEN ((p."AI_Over25_Prob" / 100.0 * m."Over25Odds") - 1) > 0
                AND m."GoalsCount" > 2
                THEN m."Over25Odds" - 1
            WHEN ((p."AI_Over25_Prob" / 100.0 * m."Over25Odds") - 1) > 0
                AND m."GoalsCount" <= 2
                THEN -1
            ELSE 0
            END AS "ProfitPer1Unit",

        -- Пример 1/4 Kelly от банка 1000
        CASE
            WHEN m."Over25Odds" > 1
                AND ((p."AI_Over25_Prob" / 100.0 * m."Over25Odds") - 1) > 0
                THEN 1000
                * (((p."AI_Over25_Prob" / 100.0 * m."Over25Odds") - 1) / (m."Over25Odds" - 1))
                * 0.25
            ELSE 0
            END AS "Stake_QuarterKelly_From1000Bank",
        CASE
            WHEN m."Over25Odds" > 1
                AND ((p."AI_Over25_Prob" / 100.0 * m."Over25Odds") - 1) > 0
                THEN 500 / (m."Over25Odds" - 1)
            ELSE 0
            END AS "BetToWin500",
        1000 as "TOPOR"
    FROM "AiPredictionsLogs" p
             JOIN "Matches" m
                  ON p."MatchId" = m."MatchId"
    WHERE
        m."Over25Odds" IS NOT NULL
      AND p."Model" = 'v2_prematch'
    ORDER BY p."Date" DESC
)
SELECT
    *,
    -- Your 3 new columns using the aliases calculated in the CTE above:
    "Stake_QuarterKelly_From1000Bank" * "ProfitPer1Unit" AS "Profit_QuarterKelly",
    "BetToWin500" * "ProfitPer1Unit" AS "Profit_BetToWin500",
    "TOPOR" * "ProfitPer1Unit" AS "Profit_TOPOR"
FROM CalculatedBets
ORDER BY "Date" DESC;