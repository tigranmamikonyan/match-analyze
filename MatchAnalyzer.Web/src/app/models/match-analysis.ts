export interface MatchAnalysis {
    stats: YearlyStats[];
    predictions: PredictionStats;
    homeForm: TeamRecentForm;
    awayForm: TeamRecentForm;
}

export interface TeamRecentForm {
    last5Goals: number[];
    avgGoals: number;
    over25Count: number;
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
