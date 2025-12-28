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
}
