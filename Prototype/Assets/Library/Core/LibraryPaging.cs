using System;
using System.Collections.Generic;
namespace SpatialWallpaper.Library {
 public sealed class LibraryPaging {
  public const int ArtworksPerPage=11;
  int total;
  public int Page {get;private set;}=1;
  public int PageCount=>Math.Max(1,(total+ArtworksPerPage-1)/ArtworksPerPage);
  public int First=>(Page-1)*ArtworksPerPage;
  public int Count=>Math.Min(ArtworksPerPage,Math.Max(0,total-First));
  public LibraryPaging(int count){SetTotal(count);}
  public void SetTotal(int count){total=Math.Max(0,count);Page=Math.Min(Page,PageCount);}
  public bool Jump(int value){if(value<1||value>PageCount)return false;Page=value;return true;}
  public static int Columns(float width)=>width>=928?4:width>=692?3:width>=456?2:1;
  // Zero denotes an inert ellipsis; every nonzero entry is a direct destination.
  public static int[] PageTokens(int page,int pages){
   var result=new List<int>();int previous=0;
   for(int i=1;i<=pages;i++)if(pages<=7||i==1||i==pages||Math.Abs(i-page)<=1){if(previous>0&&i-previous>1)result.Add(0);result.Add(i);previous=i;}
   return result.ToArray();
  }
 }
}
