import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { Match } from '../models/match';

import { MatchAnalysis } from '../models/match-analysis';

@Injectable({
  providedIn: 'root'
})
export class MatchService {
  private apiUrl = 'https://matchanalyzeapi.replyer.me/api/matches'; // Check port later

  constructor(private http: HttpClient) { }

  getMatches(date?: string): Observable<Match[]> {
    let url = this.apiUrl;
    if (date) {
      url += `?date=${date}`;
    }
    return this.http.get<Match[]>(url);
  }

  getAnalysis(id: number): Observable<MatchAnalysis> {
    return this.http.get<MatchAnalysis>(`${this.apiUrl}/${id}/analysis`);
  }

  searchMatches(request: any): Observable<Match[]> {
    return this.http.post<Match[]>(`${this.apiUrl}/search`, request);
  }

  syncMatches(days: number = 1): Observable<number> {
    return this.http.post<number>(`${this.apiUrl}/sync?days=${days}`, {});
  }

  syncUnparsedMatches(): Observable<number> {
    return this.http.post<number>(`${this.apiUrl}/sync-unparsed`, {});
  }
}

export interface SearchRequest {
  from?: string;
  to?: string;
  team?: string;
  conditions: FilterCondition[];
}

export interface FilterCondition {
  enabled: boolean;
  type: string;
  threshold: string;
  half: string;
  minPercent: number;
  maxPercent: number;
}
