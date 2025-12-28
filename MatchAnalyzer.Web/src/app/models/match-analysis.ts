export interface MatchAnalysis {
    stats: YearlyStats[];
    predictions: PredictionStats;
}

export interface YearlyStats {
    year: number;
    team: string;
    totalGames: number;
    over05: number;
    over15: number;
    over25: number;
    overFH05: number;
    overFH15: number;
    overFH25: number;
}

export interface PredictionStats {
    fullTime: string;
    firstHalf: string;
    secondHalf: string;
}
