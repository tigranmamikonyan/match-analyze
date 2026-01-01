import { Component, Input } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Match } from '../../models/match';
import { MatchAnalysis } from '../../models/match-analysis';
import { MatchService } from '../../services/match.service';

export interface ThresholdConfig {
  over05: number;
  over15: number;
  over25: number;
  overFH05: number;
  overFH15: number;
  overFH25: number;
}

@Component({
  selector: 'app-match-card',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './match-card.component.html',
  styleUrls: ['./match-card.component.scss']
})
export class MatchCardComponent {
  @Input() match!: Match;
  @Input() thresholds: ThresholdConfig = {
    over05: 90,
    over15: 80,
    over25: 70,
    overFH05: 70,
    overFH15: 60,
    overFH25: 50
  };

  analysis: MatchAnalysis | null = null;
  isLoadingAnalysis = false;
  isExpanded = false;

  constructor(private matchService: MatchService) { }

  toggleAnalysis() {
    this.isExpanded = !this.isExpanded;
    if (this.isExpanded && !this.analysis) {
      this.isLoadingAnalysis = true;
      this.matchService.getAnalysis(this.match.id).subscribe({
        next: (data) => {
          this.analysis = data;
          this.isLoadingAnalysis = false;
        },
        error: (err) => {
          console.error(err);
          this.isLoadingAnalysis = false;
        }
      });
    }
  }

  get isLive(): boolean {
    return false;
  }
}
