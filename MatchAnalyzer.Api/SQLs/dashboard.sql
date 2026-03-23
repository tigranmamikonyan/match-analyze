select *
from "Matches"
where "HomeTeamId"='xrtxGY3D' order by "Date";

select "MatchId", "Model", "AI_Over25_Prob"
from "AiPredictionsLogs" order by "MatchId", "Model";


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
        END AS "Stake_QuarterKelly_From1000Bank"

FROM "AiPredictionsLogs" p
         JOIN "Matches" m
              ON p."MatchId" = m."MatchId"
WHERE
    m."Over25Odds" IS NOT NULL
  AND m."Over25Odds" > 1
  AND p."Model" = 'v2_prematch'
ORDER BY p."Date" DESC;

select count(*)
from "Matches" where "Date"> '2026-03-21' and "Over25Odds" is not null;


