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

  toggleFavorite(event: MouseEvent, type: string) {
    event.stopPropagation();
    let currentValue = false;
    switch (type) {
      case '0.5': currentValue = this.match.isFavorite05; break;
      case '1.5': currentValue = this.match.isFavorite15; break;
      case 'fh0.5': currentValue = this.match.isFavoriteFH05; break;
      case 'fh1.5': currentValue = this.match.isFavoriteFH15; break;
    }

    const newValue = !currentValue;

    // Optimistic update
    switch (type) {
      case '0.5': this.match.isFavorite05 = newValue; break;
      case '1.5': this.match.isFavorite15 = newValue; break;
      case 'fh0.5': this.match.isFavoriteFH05 = newValue; break;
      case 'fh1.5': this.match.isFavoriteFH15 = newValue; break;
    }

    this.matchService.toggleFavorite(this.match.id, type, newValue).subscribe({
      error: () => {
        // Revert
        switch (type) {
          case '0.5': this.match.isFavorite05 = !newValue; break;
          case '1.5': this.match.isFavorite15 = !newValue; break;
          case 'fh0.5': this.match.isFavoriteFH05 = !newValue; break;
          case 'fh1.5': this.match.isFavoriteFH15 = !newValue; break;
        }
      }
    });
  }
}
