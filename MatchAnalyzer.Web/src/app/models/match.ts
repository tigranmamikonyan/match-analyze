export interface Match {
  id: number;
  matchId: string;
  homeTeam: string;
  awayTeam: string;
  date: string; // ISO string
  score?: string;
  goalsCount?: number;
  firstHalfGoals?: number;
  isParsed: boolean;
  isFavorite05: boolean;
  isFavorite15: boolean;
  isFavoriteFH05: boolean;
  isFavoriteFH15: boolean;
}
